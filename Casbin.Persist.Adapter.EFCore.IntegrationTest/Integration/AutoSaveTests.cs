using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin.Model;
using Casbin.Persist.Adapter.EFCore.Entities;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

#nullable enable

namespace Casbin.Persist.Adapter.EFCore.UnitTest.Integration
{
    /// <summary>
    /// Custom context classes for multi-schema testing
    /// </summary>
    public class TestCasbinDbContext1 : CasbinDbContext<int>
    {
        public TestCasbinDbContext1(
            DbContextOptions<CasbinDbContext<int>> options,
            string schemaName,
            string tableName)
            : base(options, schemaName, tableName)
        {
        }
    }

    public class TestCasbinDbContext2 : CasbinDbContext<int>
    {
        public TestCasbinDbContext2(
            DbContextOptions<CasbinDbContext<int>> options,
            string schemaName,
            string tableName)
            : base(options, schemaName, tableName)
        {
        }
    }

    public class TestCasbinDbContext3 : CasbinDbContext<int>
    {
        public TestCasbinDbContext3(
            DbContextOptions<CasbinDbContext<int>> options,
            string schemaName,
            string tableName)
            : base(options, schemaName, tableName)
        {
        }
    }

    /// <summary>
    /// Integration tests for AutoSave behavior using PostgreSQL.
    /// These tests verify that the adapter correctly handles AutoSave ON and OFF modes
    /// when working with both regular policies and grouping policies.
    ///
    /// Note: These are integration tests (not unit tests) because they:
    /// - Use the full Casbin Enforcer (not just the adapter in isolation)
    /// - Test the interaction between Enforcer and Adapter
    /// - Use real PostgreSQL database (not SQLite in-memory)
    /// </summary>
    [Trait("Category", "Integration")]
    [Collection("IntegrationTests")]
    public class AutoSaveTests : TestUtil, IAsyncLifetime
    {
        private readonly TransactionIntegrityTestFixture _fixture;
        private readonly ITestOutputHelper _output;
        private const string ModelPath = "examples/multi_context_model.conf";

        public AutoSaveTests(TransactionIntegrityTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        public async Task InitializeAsync()
        {
            // Clear all policies before each test
            await _fixture.ClearAllPoliciesAsync();
        }

        public async Task DisposeAsync()
        {
            // Restore any tables that may have been dropped during test execution
            await _fixture.RunMigrationsAsync();
        }

        /// <summary>
        /// Tests regular policies with AutoSave ON (default behavior).
        /// Verifies that operations immediately persist to the database.
        /// </summary>
        [Fact]
        public async Task TestPolicyAutoSaveOn()
        {
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseNpgsql(connection, b => b.MigrationsHistoryTable("__EFMigrationsHistory", TransactionIntegrityTestFixture.PoliciesSchema))
                .Options;

            await using var context = new CasbinDbContext<int>(options, schemaName: TransactionIntegrityTestFixture.PoliciesSchema);
            await InitPolicyAsync(context);

            var adapter = new EFCoreAdapter<int>(context);
            var model = DefaultModel.CreateFromText(System.IO.File.ReadAllText("examples/rbac_model.conf"));
            var enforcer = new Enforcer(model, adapter);

            #region Load policy test
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write")
            ));
            Assert.True(await context.Policies.AsNoTracking().CountAsync() is 5);
            #endregion

            #region Add policy test
            await enforcer.AddPolicyAsync("alice", "data1", "write");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write"),
                AsList("alice", "data1", "write")
            ));
            Assert.True(await context.Policies.AsNoTracking().CountAsync() is 6);
            #endregion

            #region Remove policy test
            await enforcer.RemovePolicyAsync("alice", "data1", "write");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write")
            ));
            Assert.True(await context.Policies.AsNoTracking().CountAsync() is 5);

            await enforcer.RemoveFilteredPolicyAsync(0, "data2_admin");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            ));
            Assert.True(await context.Policies.AsNoTracking().CountAsync() is 3);
            #endregion

            #region Update policy test
            await enforcer.UpdatePolicyAsync(AsList("alice", "data1", "read"),
                AsList("alice", "data2", "write"));
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data2", "write"),
                AsList("bob", "data2", "write")
            ));
            Assert.True(await context.Policies.AsNoTracking().CountAsync() is 3);

            await enforcer.UpdatePolicyAsync(AsList("alice", "data2", "write"),
                AsList("alice", "data1", "read"));
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            ));
            Assert.True(await context.Policies.AsNoTracking().CountAsync() is 3);
            #endregion

            #region Batch APIs test
            await enforcer.AddPoliciesAsync(new []
            {
                new System.Collections.Generic.List<string>{"alice", "data2", "write"},
                new System.Collections.Generic.List<string>{"bob", "data1", "read"}
            });
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("alice", "data2", "write"),
                AsList("bob", "data1", "read")
            ));
            Assert.True(await context.Policies.AsNoTracking().CountAsync() is 5);

            await enforcer.RemovePoliciesAsync(new []
            {
                new System.Collections.Generic.List<string>{"alice", "data1", "read"},
                new System.Collections.Generic.List<string>{"bob", "data2", "write"}
            });
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data2", "write"),
                AsList("bob", "data1", "read")
            ));
            Assert.True(await context.Policies.AsNoTracking().CountAsync() is 3);
            #endregion
        }

        /// <summary>
        /// Tests async version of regular policies with AutoSave ON.
        /// </summary>
        [Fact]
        public async Task TestPolicyAutoSaveOnAsync()
        {
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseNpgsql(connection, b => b.MigrationsHistoryTable("__EFMigrationsHistory", TransactionIntegrityTestFixture.PoliciesSchema))
                .Options;

            await using var context = new CasbinDbContext<int>(options, schemaName: TransactionIntegrityTestFixture.PoliciesSchema);
            await InitPolicyAsync(context);

            var adapter = new EFCoreAdapter<int>(context);
            var model = DefaultModel.CreateFromText(System.IO.File.ReadAllText("examples/rbac_model.conf"));
            var enforcer = new Enforcer(model, adapter);

            #region Load policy test
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write")
            ));
            Assert.True(await context.Policies.AsNoTracking().CountAsync() is 5);
            #endregion

            #region Add policy test
            await enforcer.AddPolicyAsync("alice", "data1", "write");
            Assert.True(await context.Policies.AsNoTracking().CountAsync() is 6);
            #endregion

            #region Remove policy test
            await enforcer.RemovePolicyAsync("alice", "data1", "write");
            Assert.True(await context.Policies.AsNoTracking().CountAsync() is 5);
            #endregion
        }

        /// <summary>
        /// Tests regular policies with AutoSave OFF.
        /// Verifies that AddPolicy() correctly respects AutoSave OFF setting.
        /// This documents the CORRECT behavior (contrast with grouping policy bug).
        /// </summary>
        [Fact]
        public async Task TestPolicyAutoSaveOff()
        {
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseNpgsql(connection, b => b.MigrationsHistoryTable("__EFMigrationsHistory", TransactionIntegrityTestFixture.PoliciesSchema))
                .Options;

            await using var context = new CasbinDbContext<int>(options, schemaName: TransactionIntegrityTestFixture.PoliciesSchema);
            await InitPolicyAsync(context);

            var adapter = new EFCoreAdapter<int>(context);
            var model = DefaultModel.CreateFromText(System.IO.File.ReadAllText("examples/rbac_model.conf"));
            var enforcer = new Enforcer(model, adapter);

            // Disable AutoSave
            enforcer.EnableAutoSave(false);

            // Verify initial state
            Assert.Equal(5, await context.Policies.AsNoTracking().CountAsync());

            // Add policy - should NOT save to database with AutoSave OFF
            enforcer.AddPolicy("charlie", "data3", "read");

            // Verify policy was NOT saved yet (correct behavior for regular policies)
            var countAfterAdd = await context.Policies.AsNoTracking().CountAsync();
            Assert.Equal(5, countAfterAdd); // Still 5 - CORRECT BEHAVIOR

            // Verify policy is NOT in database yet
            var charlieBeforeSave = await context.Policies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Type == "p" && p.Value1 == "charlie");
            Assert.Null(charlieBeforeSave); // Not in database yet - CORRECT

            // When SavePolicy is called, it should save the policy
            await enforcer.SavePolicyAsync();

            // Now the policy should be in database
            Assert.Equal(6, await context.Policies.AsNoTracking().CountAsync()); // 5 + 1 = 6

            // Verify it's in database after SavePolicy
            var charlieAfterSave = await context.Policies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Type == "p" && p.Value1 == "charlie");
            Assert.NotNull(charlieAfterSave);
        }

        /// <summary>
        /// Tests async version of regular policies with AutoSave OFF.
        /// Verifies that AddPolicyAsync() correctly respects AutoSave OFF setting.
        /// </summary>
        [Fact]
        public async Task TestPolicyAutoSaveOffAsync()
        {
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseNpgsql(connection, b => b.MigrationsHistoryTable("__EFMigrationsHistory", TransactionIntegrityTestFixture.PoliciesSchema))
                .Options;

            await using var context = new CasbinDbContext<int>(options, schemaName: TransactionIntegrityTestFixture.PoliciesSchema);
            await InitPolicyAsync(context);

            var adapter = new EFCoreAdapter<int>(context);
            var model = DefaultModel.CreateFromText(System.IO.File.ReadAllText("examples/rbac_model.conf"));
            var enforcer = new Enforcer(model, adapter);

            // Disable AutoSave
            enforcer.EnableAutoSave(false);

            // Verify initial state
            Assert.Equal(5, await context.Policies.AsNoTracking().CountAsync());

            // Add policy - should NOT save to database with AutoSave OFF
            await enforcer.AddPolicyAsync("charlie", "data3", "read");

            // Verify policy was NOT saved yet (correct behavior)
            var countAfterAdd = await context.Policies.AsNoTracking().CountAsync();
            Assert.Equal(5, countAfterAdd); // Still 5 - CORRECT BEHAVIOR

            // When SavePolicy is called, it should save the policy
            await enforcer.SavePolicyAsync();

            // Now the policy should be in database
            Assert.Equal(6, await context.Policies.AsNoTracking().CountAsync());

            // Verify it's in database
            var charlieAfterSave = await context.Policies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Type == "p" && p.Value1 == "charlie");
            Assert.NotNull(charlieAfterSave);
        }

        /// <summary>
        /// Tests grouping policies with AutoSave ON (default behavior).
        /// This test verifies that AddGroupingPolicy() immediately saves to database.
        /// </summary>
        [Fact]
        public async Task TestGroupingPolicyAutoSaveOn()
        {
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseNpgsql(connection, b => b.MigrationsHistoryTable("__EFMigrationsHistory", TransactionIntegrityTestFixture.GroupingsSchema))
                .Options;

            await using var context = new CasbinDbContext<int>(options, schemaName: TransactionIntegrityTestFixture.GroupingsSchema);
            await InitPolicyAsync(context);

            var adapter = new EFCoreAdapter<int>(context);
            var model = DefaultModel.CreateFromText(System.IO.File.ReadAllText("examples/rbac_model.conf"));
            var enforcer = new Enforcer(model, adapter);

            // Verify initial grouping policy
            TestGetGroupingPolicy(enforcer, AsList(
                AsList("alice", "data2_admin")
            ));
            Assert.Equal(5, await context.Policies.AsNoTracking().CountAsync());

            // Add grouping policy - should save immediately with AutoSave ON
            await enforcer.AddGroupingPolicyAsync("bob", "data2_admin");

            // Verify it's in Casbin's memory
            TestGetGroupingPolicy(enforcer, AsList(
                AsList("alice", "data2_admin"),
                AsList("bob", "data2_admin")
            ));

            // Verify it was saved to database immediately
            Assert.Equal(6, await context.Policies.AsNoTracking().CountAsync());
            var bobGrouping = await context.Policies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Type == "g" && p.Value1 == "bob" && p.Value2 == "data2_admin");
            Assert.NotNull(bobGrouping);
        }

        /// <summary>
        /// Tests grouping policies with AutoSave OFF.
        ///
        /// Verifies that AddGroupingPolicy() respects the EnableAutoSave(false) setting.
        ///
        /// Expected behavior (verified by this test):
        /// - AddGroupingPolicy() should NOT save to database when AutoSave is OFF
        /// - Only SavePolicy() should commit changes
        ///
        /// This test now passes with Casbin.NET 2.19.1+ which fixed the AutoSave bug.
        ///
        /// Related: Integration/README.md
        /// </summary>
        [Fact]
        public async Task TestGroupingPolicyAutoSaveOff()
        {
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseNpgsql(connection, b => b.MigrationsHistoryTable("__EFMigrationsHistory", TransactionIntegrityTestFixture.GroupingsSchema))
                .Options;

            await using var context = new CasbinDbContext<int>(options, schemaName: TransactionIntegrityTestFixture.GroupingsSchema);
            await InitPolicyAsync(context);

            var adapter = new EFCoreAdapter<int>(context);
            var model = DefaultModel.CreateFromText(System.IO.File.ReadAllText("examples/rbac_model.conf"));
            var enforcer = new Enforcer(model, adapter);

            // Disable AutoSave
            enforcer.EnableAutoSave(false);

            // Verify initial state
            Assert.Equal(5, await context.Policies.AsNoTracking().CountAsync());

            // Add regular policy - should NOT save to database with AutoSave OFF
            enforcer.AddPolicy("charlie", "data3", "read");

            // Verify regular policy was NOT saved (correct behavior)
            Assert.Equal(5, await context.Policies.AsNoTracking().CountAsync());

            // Add grouping policy - should NOT save to database with AutoSave OFF
            await enforcer.AddGroupingPolicyAsync("bob", "data2_admin");

            // TEST EXPECTATION: Grouping policy should NOT be saved yet (AutoSave is OFF)
            // BUG: This will fail because Casbin.NET incorrectly saves it (Actual: 6, Expected: 5)
            var savedCountAfterAdd = await context.Policies.AsNoTracking().CountAsync();
            Assert.Equal(5, savedCountAfterAdd); // FAILS due to bug: actual is 6

            // Verify the grouping policy is NOT in database yet
            var bobGroupingBeforeSave = await context.Policies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Type == "g" && p.Value1 == "bob" && p.Value2 == "data2_admin");
            Assert.Null(bobGroupingBeforeSave); // FAILS due to bug: it exists

            // When SavePolicy is called, it should save BOTH policies
            await enforcer.SavePolicyAsync();

            // Now both policies should be in database
            Assert.Equal(7, await context.Policies.AsNoTracking().CountAsync()); // 5 original + 1 charlie + 1 bob = 7

            // Verify both are in database after SavePolicy
            var charliePolicy = await context.Policies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Type == "p" && p.Value1 == "charlie");
            var bobGroupingAfterSave = await context.Policies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Type == "g" && p.Value1 == "bob" && p.Value2 == "data2_admin");

            Assert.NotNull(charliePolicy);
            Assert.NotNull(bobGroupingAfterSave);
        }

        /// <summary>
        /// Tests async version of grouping policies with AutoSave OFF.
        ///
        /// Verifies that AddGroupingPolicyAsync() respects the EnableAutoSave(false) setting.
        ///
        /// Expected behavior (verified by this test): AddGroupingPolicyAsync() should NOT save when AutoSave is OFF.
        ///
        /// This test now passes with Casbin.NET 2.19.1+ which fixed the AutoSave bug.
        /// </summary>
        [Fact]
        public async Task TestGroupingPolicyAutoSaveOffAsync()
        {
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseNpgsql(connection, b => b.MigrationsHistoryTable("__EFMigrationsHistory", TransactionIntegrityTestFixture.GroupingsSchema))
                .Options;

            await using var context = new CasbinDbContext<int>(options, schemaName: TransactionIntegrityTestFixture.GroupingsSchema);
            await InitPolicyAsync(context);

            var adapter = new EFCoreAdapter<int>(context);
            var model = DefaultModel.CreateFromText(System.IO.File.ReadAllText("examples/rbac_model.conf"));
            var enforcer = new Enforcer(model, adapter);

            // Disable AutoSave
            enforcer.EnableAutoSave(false);

            // Verify initial state
            Assert.Equal(5, await context.Policies.AsNoTracking().CountAsync());

            // Add regular policy - should NOT save to database with AutoSave OFF
            await enforcer.AddPolicyAsync("charlie", "data3", "read");

            // Verify regular policy was NOT saved (correct behavior)
            Assert.Equal(5, await context.Policies.AsNoTracking().CountAsync());

            // Add grouping policy - should NOT save to database with AutoSave OFF
            await enforcer.AddGroupingPolicyAsync("bob", "data2_admin");

            // TEST EXPECTATION: Grouping policy should NOT be saved yet
            // BUG: This will fail because Casbin.NET incorrectly saves it (Actual: 6, Expected: 5)
            var savedCountAfterAdd = await context.Policies.AsNoTracking().CountAsync();
            Assert.Equal(5, savedCountAfterAdd); // FAILS due to bug: actual is 6

            // When SavePolicy is called, it should save BOTH policies
            await enforcer.SavePolicyAsync();

            // Now both policies should be in database
            Assert.Equal(7, await context.Policies.AsNoTracking().CountAsync()); // 5 original + 1 charlie + 1 bob = 7

            // Verify both are in database
            var charliePolicy = await context.Policies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Type == "p" && p.Value1 == "charlie");
            var bobGrouping = await context.Policies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Type == "g" && p.Value1 == "bob" && p.Value2 == "data2_admin");

            Assert.NotNull(charliePolicy);
            Assert.NotNull(bobGrouping);
        }

        #region Multi-Context AutoSave Tests

        /// <summary>
        /// Provider that routes policy types to three separate contexts
        /// </summary>
        private class ThreeWayContextProvider : ICasbinDbContextProvider<int>
        {
            private readonly CasbinDbContext<int> _policyContext;
            private readonly CasbinDbContext<int> _groupingContext;
            private readonly CasbinDbContext<int> _roleContext;
            private readonly System.Data.Common.DbConnection? _sharedConnection;

            public ThreeWayContextProvider(
                CasbinDbContext<int> policyContext,
                CasbinDbContext<int> groupingContext,
                CasbinDbContext<int> roleContext,
                System.Data.Common.DbConnection? sharedConnection)
            {
                _policyContext = policyContext;
                _groupingContext = groupingContext;
                _roleContext = roleContext;
                _sharedConnection = sharedConnection;
            }

            public DbContext GetContextForPolicyType(string policyType)
            {
                return policyType switch
                {
                    "p" => _policyContext,      // p policies → casbin_policies schema
                    "g" => _groupingContext,     // g groupings → casbin_groupings schema
                    "g2" => _roleContext,        // g2 roles → casbin_roles schema
                    _ => _policyContext
                };
            }

            public IEnumerable<DbContext> GetAllContexts()
            {
                return new[] { _policyContext, _groupingContext, _roleContext };
            }

            public System.Data.Common.DbConnection? GetSharedConnection()
            {
                return _sharedConnection;
            }
        }

        /// <summary>
        /// Tests AutoSave OFF with multiple contexts and rollback on failure.
        ///
        /// Verifies that:
        /// - With AutoSave OFF, policies batch in memory (not commit)
        /// - SavePolicy() uses shared transaction and rolls back atomically on failure
        ///
        /// This test now passes with Casbin.NET 2.19.1+ which fixed the AutoSave bug.
        /// </summary>
        [Fact]
        public async Task TestAutoSaveOff_MultiContext_RollbackOnFailure()
        {
            _output.WriteLine("=== AUTOSAVE OFF - MULTI-CONTEXT ATOMIC ROLLBACK TEST ===");
            _output.WriteLine("Goal: With AutoSave OFF, SavePolicy should use shared transaction and rollback atomically");
            _output.WriteLine("");

            // Clear all data first
            await _fixture.ClearAllPoliciesAsync();

            // Create ONE shared connection
            var sharedConnection = new NpgsqlConnection(_fixture.ConnectionString);
            await sharedConnection.OpenAsync();

            try
            {
                _output.WriteLine($"Shared connection: {sharedConnection.GetHashCode()}");
                _output.WriteLine("");

                // Create three contexts using the SAME connection object
                var options1 = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                    .UseNpgsql(sharedConnection)
                    .Options;
                var policyContext = new TestCasbinDbContext1(options1, TransactionIntegrityTestFixture.PoliciesSchema, "casbin_rule");

                var options2 = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                    .UseNpgsql(sharedConnection)
                    .Options;
                var groupingContext = new TestCasbinDbContext2(options2, TransactionIntegrityTestFixture.GroupingsSchema, "casbin_rule");

                var options3 = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                    .UseNpgsql(sharedConnection)
                    .Options;
                var roleContext = new TestCasbinDbContext3(options3, TransactionIntegrityTestFixture.RolesSchema, "casbin_rule");

                // Create provider and adapter
                var provider = new ThreeWayContextProvider(policyContext, groupingContext, roleContext, sharedConnection);
                var adapter = new EFCoreAdapter<int>(provider);

                // Create enforcer and DISABLE AutoSave
                var model = DefaultModel.CreateFromFile(ModelPath);
                var enforcer = new Enforcer(model);
                enforcer.SetAdapter(adapter);
                enforcer.EnableAutoSave(false);  // ← CRITICAL: Disable AutoSave
                _output.WriteLine("AutoSave disabled");
                _output.WriteLine("");

                // Add multiple policies to each type
                _output.WriteLine("Adding policies with AutoSave OFF (should batch in memory):");
                enforcer.AddPolicy("alice", "data1", "read");
                enforcer.AddPolicy("bob", "data2", "write");
                _output.WriteLine("  Added 2 p policies");

                enforcer.AddGroupingPolicy("alice", "admin");
                enforcer.AddGroupingPolicy("bob", "user");
                _output.WriteLine("  Added 2 g groupings");

                enforcer.AddNamedGroupingPolicy("g2", "admin", "role-superuser");
                enforcer.AddNamedGroupingPolicy("g2", "user", "role-basic");
                _output.WriteLine("  Added 2 g2 roles");
                _output.WriteLine("");

                // Check database state - should be EMPTY (policies batched, not committed)
                var beforePoliciesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                var beforeGroupingsCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);
                var beforeRolesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.RolesSchema);
                _output.WriteLine($"STATE BEFORE DROP (should be 0,0,0): ({beforePoliciesCount}, {beforeGroupingsCount}, {beforeRolesCount})");

                if (beforePoliciesCount == 0 && beforeGroupingsCount == 0 && beforeRolesCount == 0)
                {
                    _output.WriteLine("✓ Confirmed: AutoSave OFF prevents immediate commits");
                }
                else
                {
                    _output.WriteLine("✗ WARNING: Policies were committed despite AutoSave OFF!");
                }
                _output.WriteLine("");

                // NOW: Drop the table for the third schema to force a failure
                _output.WriteLine("FORCING FAILURE: Dropping casbin_roles.casbin_rule table...");
                await using (var cmd = sharedConnection.CreateCommand())
                {
                    cmd.CommandText = $"DROP TABLE {TransactionIntegrityTestFixture.RolesSchema}.casbin_rule";
                    await cmd.ExecuteNonQueryAsync();
                }
                _output.WriteLine("Table dropped!");
                _output.WriteLine("");

                // Try to save - this SHOULD fail
                _output.WriteLine("Calling SavePolicyAsync()... (expecting exception)");
                var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
                {
                    await enforcer.SavePolicyAsync();
                });
                _output.WriteLine($"✓ Exception caught as expected: {exception.GetType().Name}");
                _output.WriteLine($"  Message: {exception.Message}");
                _output.WriteLine("");

                // Count policies in the first two schemas (third schema table is gone)
                var policiesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);

                _output.WriteLine("RESULTS - Policies per schema after failure:");
                _output.WriteLine($"  casbin_policies:  {policiesCount}");
                _output.WriteLine($"  casbin_groupings: {groupingsCount}");
                _output.WriteLine($"  casbin_roles:     N/A (table dropped)");
                _output.WriteLine("");

                // ASSERT: With AutoSave OFF and shared connection, SavePolicy should roll back ALL changes
                if (policiesCount == 0 && groupingsCount == 0)
                {
                    _output.WriteLine("✓✓✓ AUTOSAVE OFF ATOMIC TRANSACTION TEST PASSED!");
                    _output.WriteLine("SavePolicy used shared transaction and rolled back atomically");
                }
                else
                {
                    _output.WriteLine("✗✗✗ AUTOSAVE OFF ATOMIC TRANSACTION TEST FAILED!");
                    _output.WriteLine($"Expected: (0, 0), Got: ({policiesCount}, {groupingsCount})");
                }

                Assert.Equal(0, policiesCount);
                Assert.Equal(0, groupingsCount);

                await policyContext.DisposeAsync();
                await groupingContext.DisposeAsync();
                await roleContext.DisposeAsync();
            }
            finally
            {
                await sharedConnection.DisposeAsync();
            }
        }

        /// <summary>
        /// Tests AutoSave ON with multiple contexts showing individual commits.
        /// Verifies that each AddPolicy commits independently (no cross-context atomicity).
        /// </summary>
        [Fact]
        public async Task TestAutoSaveOn_MultiContext_IndividualCommits()
        {
            _output.WriteLine("=== AUTOSAVE ON - INDIVIDUAL COMMITS TEST ===");
            _output.WriteLine("Goal: With AutoSave ON, each Add should commit independently (no atomicity)");
            _output.WriteLine("");

            // Clear all data first
            await _fixture.ClearAllPoliciesAsync();

            // Create ONE shared connection
            var sharedConnection = new NpgsqlConnection(_fixture.ConnectionString);
            await sharedConnection.OpenAsync();

            try
            {
                // Create three contexts using the SAME connection object
                var options1 = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                    .UseNpgsql(sharedConnection)
                    .Options;
                var policyContext = new TestCasbinDbContext1(options1, TransactionIntegrityTestFixture.PoliciesSchema, "casbin_rule");

                var options2 = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                    .UseNpgsql(sharedConnection)
                    .Options;
                var groupingContext = new TestCasbinDbContext2(options2, TransactionIntegrityTestFixture.GroupingsSchema, "casbin_rule");

                var options3 = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                    .UseNpgsql(sharedConnection)
                    .Options;
                var roleContext = new TestCasbinDbContext3(options3, TransactionIntegrityTestFixture.RolesSchema, "casbin_rule");

                // Create provider and adapter
                var provider = new ThreeWayContextProvider(policyContext, groupingContext, roleContext, sharedConnection);
                var adapter = new EFCoreAdapter<int>(provider);

                // Create enforcer with AutoSave ON (default)
                var model = DefaultModel.CreateFromFile(ModelPath);
                var enforcer = new Enforcer(model);
                enforcer.SetAdapter(adapter);
                // enforcer.EnableAutoSave(true); // Default is true, no need to set
                _output.WriteLine("AutoSave enabled (default)");
                _output.WriteLine("");

                // Add policies to context 1 and check DB immediately
                _output.WriteLine("Step 1: Adding 2 p policies (should commit immediately):");
                enforcer.AddPolicy("alice", "data1", "read");
                enforcer.AddPolicy("bob", "data2", "write");
                var step1Count = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                _output.WriteLine($"  DB state after p policies: ({step1Count}, ?, ?)");
                Assert.Equal(2, step1Count);
                _output.WriteLine("");

                // Add policies to context 2 and check DB immediately
                _output.WriteLine("Step 2: Adding 2 g policies (should commit immediately):");
                enforcer.AddGroupingPolicy("alice", "admin");
                enforcer.AddGroupingPolicy("bob", "user");
                var step2CountPolicy = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                var step2CountGrouping = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);
                _output.WriteLine($"  DB state after g policies: ({step2CountPolicy}, {step2CountGrouping}, ?)");
                Assert.Equal(2, step2CountPolicy);
                Assert.Equal(2, step2CountGrouping);
                _output.WriteLine("");

                // NOW: Drop the table for the third schema
                _output.WriteLine("Step 3: Dropping casbin_roles.casbin_rule table...");
                await using (var cmd = sharedConnection.CreateCommand())
                {
                    cmd.CommandText = $"DROP TABLE {TransactionIntegrityTestFixture.RolesSchema}.casbin_rule";
                    await cmd.ExecuteNonQueryAsync();
                }
                _output.WriteLine("Table dropped!");
                _output.WriteLine("");

                // Try to add policy to context 3 - should fail
                _output.WriteLine("Step 4: Trying to add g2 policy (expecting exception):");
                var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
                {
                    await enforcer.AddNamedGroupingPolicyAsync("g2", "admin", "role-superuser");
                });
                _output.WriteLine($"✓ Exception caught as expected: {exception.GetType().Name}");
                _output.WriteLine($"  Message: {exception.Message}");
                _output.WriteLine("");

                // Check final state - contexts 1 & 2 should still have their data
                var finalPoliciesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                var finalGroupingsCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);

                _output.WriteLine("FINAL RESULTS:");
                _output.WriteLine($"  casbin_policies:  {finalPoliciesCount}");
                _output.WriteLine($"  casbin_groupings: {finalGroupingsCount}");
                _output.WriteLine($"  casbin_roles:     N/A (table dropped)");
                _output.WriteLine("");

                // ASSERT: With AutoSave ON, each context committed independently
                if (finalPoliciesCount == 2 && finalGroupingsCount == 2)
                {
                    _output.WriteLine("✓✓✓ AUTOSAVE ON INDIVIDUAL COMMITS TEST PASSED!");
                    _output.WriteLine("Each AddPolicy committed independently, no cross-context atomicity");
                }
                else
                {
                    _output.WriteLine("✗✗✗ AUTOSAVE ON INDIVIDUAL COMMITS TEST FAILED!");
                    _output.WriteLine($"Expected: (2, 2), Got: ({finalPoliciesCount}, {finalGroupingsCount})");
                }

                Assert.Equal(2, finalPoliciesCount);
                Assert.Equal(2, finalGroupingsCount);

                await policyContext.DisposeAsync();
                await groupingContext.DisposeAsync();
                await roleContext.DisposeAsync();
            }
            finally
            {
                await sharedConnection.DisposeAsync();
            }
        }

        /// <summary>
        /// Tests AutoSave OFF success path with batched commit across multiple contexts.
        ///
        /// Verifies that with AutoSave OFF, SavePolicy() batches all operations
        /// in a shared transaction and commits atomically.
        ///
        /// This test now passes with Casbin.NET 2.19.1+ which fixed the AutoSave bug.
        /// </summary>
        [Fact]
        public async Task TestAutoSaveOff_MultiContext_BatchedCommit()
        {
            _output.WriteLine("=== AUTOSAVE OFF - SUCCESS PATH TEST ===");
            _output.WriteLine("Goal: With AutoSave OFF, SavePolicy should batch all operations in shared transaction");
            _output.WriteLine("");

            // Clear all data first
            await _fixture.ClearAllPoliciesAsync();

            // Create ONE shared connection
            var sharedConnection = new NpgsqlConnection(_fixture.ConnectionString);
            await sharedConnection.OpenAsync();

            try
            {
                // Create three contexts
                var options1 = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                    .UseNpgsql(sharedConnection)
                    .Options;
                var policyContext = new TestCasbinDbContext1(options1, TransactionIntegrityTestFixture.PoliciesSchema, "casbin_rule");

                var options2 = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                    .UseNpgsql(sharedConnection)
                    .Options;
                var groupingContext = new TestCasbinDbContext2(options2, TransactionIntegrityTestFixture.GroupingsSchema, "casbin_rule");

                var options3 = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                    .UseNpgsql(sharedConnection)
                    .Options;
                var roleContext = new TestCasbinDbContext3(options3, TransactionIntegrityTestFixture.RolesSchema, "casbin_rule");

                var provider = new ThreeWayContextProvider(policyContext, groupingContext, roleContext, sharedConnection);
                var adapter = new EFCoreAdapter<int>(provider);

                var model = DefaultModel.CreateFromFile(ModelPath);
                var enforcer = new Enforcer(model);
                enforcer.SetAdapter(adapter);
                enforcer.EnableAutoSave(false);
                _output.WriteLine("AutoSave disabled");
                _output.WriteLine("");

                // Add policies
                _output.WriteLine("Adding policies with AutoSave OFF:");
                enforcer.AddPolicy("alice", "data1", "read");
                enforcer.AddPolicy("bob", "data2", "write");
                enforcer.AddGroupingPolicy("alice", "admin");
                enforcer.AddGroupingPolicy("bob", "user");
                enforcer.AddNamedGroupingPolicy("g2", "admin", "role-superuser");
                enforcer.AddNamedGroupingPolicy("g2", "user", "role-basic");
                _output.WriteLine("  Added 6 policies total");
                _output.WriteLine("");

                // Check DB before SavePolicy
                var beforeCount1 = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                var beforeCount2 = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);
                var beforeCount3 = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.RolesSchema);
                _output.WriteLine($"DB state BEFORE SavePolicy: ({beforeCount1}, {beforeCount2}, {beforeCount3})");

                if (beforeCount1 == 0 && beforeCount2 == 0 && beforeCount3 == 0)
                {
                    _output.WriteLine("✓ Confirmed: Policies batched in memory, not committed yet");
                }
                _output.WriteLine("");

                // Call SavePolicy
                _output.WriteLine("Calling SavePolicyAsync()...");
                await enforcer.SavePolicyAsync();
                _output.WriteLine("SavePolicyAsync() completed");
                _output.WriteLine("");

                // Check final state
                var finalCount1 = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                var finalCount2 = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);
                var finalCount3 = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.RolesSchema);

                _output.WriteLine($"DB state AFTER SavePolicy: ({finalCount1}, {finalCount2}, {finalCount3})");
                _output.WriteLine("");

                if (finalCount1 == 2 && finalCount2 == 2 && finalCount3 == 2)
                {
                    _output.WriteLine("✓✓✓ AUTOSAVE OFF SUCCESS PATH TEST PASSED!");
                    _output.WriteLine("SavePolicy committed all batched policies atomically");
                }
                else
                {
                    _output.WriteLine("✗✗✗ AUTOSAVE OFF SUCCESS PATH TEST FAILED!");
                    _output.WriteLine($"Expected: (2, 2, 2), Got: ({finalCount1}, {finalCount2}, {finalCount3})");
                }

                Assert.Equal(2, finalCount1);
                Assert.Equal(2, finalCount2);
                Assert.Equal(2, finalCount3);

                await policyContext.DisposeAsync();
                await groupingContext.DisposeAsync();
                await roleContext.DisposeAsync();
            }
            finally
            {
                await sharedConnection.DisposeAsync();
            }
        }

        #endregion

        private static async Task InitPolicyAsync(CasbinDbContext<int> context)
        {
            // Clear existing policies - use AsNoTracking to avoid concurrency exceptions
            // when policies may have been deleted by fixture cleanup
            var existing = await context.Policies.AsNoTracking().ToListAsync();
            if (existing.Any())
            {
                context.Policies.AttachRange(existing);
                context.Policies.RemoveRange(existing);
                await context.SaveChangesAsync();
            }

            // Add test data
            context.Policies.Add(new EFCorePersistPolicy<int>()
            {
                Type = "p",
                Value1 = "alice",
                Value2 = "data1",
                Value3 = "read",
            });
            context.Policies.Add(new EFCorePersistPolicy<int>()
            {
                Type = "p",
                Value1 = "bob",
                Value2 = "data2",
                Value3 = "write",
            });
            context.Policies.Add(new EFCorePersistPolicy<int>()
            {
                Type = "p",
                Value1 = "data2_admin",
                Value2 = "data2",
                Value3 = "read",
            });
            context.Policies.Add(new EFCorePersistPolicy<int>()
            {
                Type = "p",
                Value1 = "data2_admin",
                Value2 = "data2",
                Value3 = "write",
            });
            context.Policies.Add(new EFCorePersistPolicy<int>()
            {
                Type = "g",
                Value1 = "alice",
                Value2 = "data2_admin",
            });
            await context.SaveChangesAsync();
        }
    }
}
