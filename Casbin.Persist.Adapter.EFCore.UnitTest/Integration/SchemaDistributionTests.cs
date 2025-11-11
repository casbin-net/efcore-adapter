using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin;
using Casbin.Model;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Casbin.Persist.Adapter.EFCore.UnitTest.Integration
{
    /// <summary>
    /// Tests to verify whether HasDefaultSchema() properly distributes policies across PostgreSQL schemas,
    /// both with separate connections and with shared connections.
    ///
    /// Purpose: Determine if explicit SET search_path is necessary or if EF Core's HasDefaultSchema()
    /// generates schema-qualified SQL that works correctly with shared connections.
    /// </summary>
    [Trait("Category", "Integration")]
    [Collection("IntegrationTests")]
    public class SchemaDistributionTests : IClassFixture<TransactionIntegrityTestFixture>, IAsyncLifetime
    {
        private readonly TransactionIntegrityTestFixture _fixture;
        private readonly ITestOutputHelper _output;
        private const string ModelPath = "examples/multi_context_model.conf";

        public SchemaDistributionTests(TransactionIntegrityTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        public Task InitializeAsync() => _fixture.ClearAllPoliciesAsync();
        public Task DisposeAsync() => _fixture.RunMigrationsAsync();

        #region Helper: Derived Context Classes

        /// <summary>
        /// Derived context for policies schema
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

        /// <summary>
        /// Derived context for groupings schema
        /// </summary>
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

        /// <summary>
        /// Derived context for roles schema
        /// </summary>
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

        #endregion

        #region Helper: Three-Context Provider

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

        #endregion

        #region Test 1: Separate Connections (Control/Baseline)

        /// <summary>
        /// BASELINE TEST: Proves that HasDefaultSchema() correctly distributes policies across schemas
        /// when contexts use SEPARATE connections (no shared connection).
        ///
        /// This is the baseline that should work regardless of any SET search_path logic.
        /// </summary>
        [Fact]
        public async Task SavePolicy_SeparateConnections_ShouldDistributeAcrossSchemas()
        {
            _output.WriteLine("=== TEST: Separate Connections - Schema Distribution ===");

            // Create three contexts with SEPARATE connection strings (no shared connection)
            var policyOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseNpgsql(_fixture.ConnectionString)  // Connection #1
                .Options;
            var policyContext = new TestCasbinDbContext1(policyOptions, TransactionIntegrityTestFixture.PoliciesSchema, "casbin_rule");

            var groupingOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseNpgsql(_fixture.ConnectionString)  // Connection #2
                .Options;
            var groupingContext = new TestCasbinDbContext2(groupingOptions, TransactionIntegrityTestFixture.GroupingsSchema, "casbin_rule");

            var roleOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseNpgsql(_fixture.ConnectionString)  // Connection #3
                .Options;
            var roleContext = new TestCasbinDbContext3(roleOptions, TransactionIntegrityTestFixture.RolesSchema, "casbin_rule");

            _output.WriteLine("Created three contexts with SEPARATE connections");

            // Verify they are different connection objects
            var conn1 = policyContext.Database.GetDbConnection();
            var conn2 = groupingContext.Database.GetDbConnection();
            var conn3 = roleContext.Database.GetDbConnection();

            Assert.False(ReferenceEquals(conn1, conn2), "Connections 1 and 2 should be different objects");
            Assert.False(ReferenceEquals(conn2, conn3), "Connections 2 and 3 should be different objects");
            _output.WriteLine("Verified: Contexts use DIFFERENT DbConnection objects");

            try
            {
                // Create provider and adapter
                // Pass null since these contexts use separate connections
                var provider = new ThreeWayContextProvider(policyContext, groupingContext, roleContext, null);
                var adapter = new EFCoreAdapter<int>(provider);

                // Create enforcer without loading policy (tables might be empty)
                var model = DefaultModel.CreateFromFile(ModelPath);
                var enforcer = new Enforcer(model);
                enforcer.SetAdapter(adapter);

                // Add policies to in-memory model (not persisted yet)
                enforcer.AddPolicy("alice", "data1", "read");      // → casbin_policies
                enforcer.AddGroupingPolicy("alice", "admin");       // → casbin_groupings
                enforcer.AddNamedGroupingPolicy("g2", "admin", "role-superuser"); // → casbin_roles

                _output.WriteLine("Added policies to in-memory model:");
                _output.WriteLine("  p policy → casbin_policies");
                _output.WriteLine("  g policy → casbin_groupings");
                _output.WriteLine("  g2 policy → casbin_roles");

                // Save to database
                await enforcer.SavePolicyAsync();
                _output.WriteLine("Called SavePolicyAsync()");

                // Verify distribution across schemas
                var policiesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);
                var rolesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.RolesSchema);

                _output.WriteLine($"Schema distribution:");
                _output.WriteLine($"  casbin_policies: {policiesCount} policy");
                _output.WriteLine($"  casbin_groupings: {groupingsCount} policy");
                _output.WriteLine($"  casbin_roles: {rolesCount} policy");

                // CRITICAL ASSERTION: Policies should be distributed across all three schemas
                Assert.Equal(1, policiesCount);
                Assert.Equal(1, groupingsCount);
                Assert.Equal(1, rolesCount);

                _output.WriteLine("✓ BASELINE TEST PASSED: HasDefaultSchema() distributes policies correctly with separate connections");
            }
            finally
            {
                await policyContext.DisposeAsync();
                await groupingContext.DisposeAsync();
                await roleContext.DisposeAsync();
            }
        }

        #endregion

        #region Test 2: Shared Connection (Critical Test)

        /// <summary>
        /// CRITICAL TEST: Determines if HasDefaultSchema() correctly distributes policies across schemas
        /// when contexts share a SINGLE DbConnection object.
        ///
        /// If this test FAILS: SET search_path approach is necessary
        /// If this test PASSES: SET search_path approach is NOT necessary
        /// </summary>
        [Fact]
        public async Task SavePolicy_SharedConnection_ShouldDistributeAcrossSchemas()
        {
            _output.WriteLine("=== TEST: Shared Connection - Schema Distribution ===");

            // Create ONE shared connection (CRITICAL for this test)
            var sharedConnection = new NpgsqlConnection(_fixture.ConnectionString);
            await sharedConnection.OpenAsync();
            _output.WriteLine("Opened shared connection for all three contexts");

            try
            {
                // Create three contexts using SAME connection object
                var policyOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                    .UseNpgsql(sharedConnection)  // ✅ Shared connection
                    .Options;
                var policyContext = new TestCasbinDbContext1(policyOptions, TransactionIntegrityTestFixture.PoliciesSchema, "casbin_rule");

                var groupingOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                    .UseNpgsql(sharedConnection)  // ✅ Same connection
                    .Options;
                var groupingContext = new TestCasbinDbContext2(groupingOptions, TransactionIntegrityTestFixture.GroupingsSchema, "casbin_rule");

                var roleOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                    .UseNpgsql(sharedConnection)  // ✅ Same connection
                    .Options;
                var roleContext = new TestCasbinDbContext3(roleOptions, TransactionIntegrityTestFixture.RolesSchema, "casbin_rule");

                _output.WriteLine("Created three contexts sharing the same connection");

                // Verify reference equality
                var conn1 = policyContext.Database.GetDbConnection();
                var conn2 = groupingContext.Database.GetDbConnection();
                var conn3 = roleContext.Database.GetDbConnection();

                Assert.True(ReferenceEquals(conn1, conn2), "Connections 1 and 2 should be the SAME object");
                Assert.True(ReferenceEquals(conn2, conn3), "Connections 2 and 3 should be the SAME object");
                _output.WriteLine("Verified: All contexts share the SAME DbConnection object (reference equality)");

                // Create provider and adapter
                // Pass sharedConnection since all contexts share it
                var provider = new ThreeWayContextProvider(policyContext, groupingContext, roleContext, sharedConnection);
                var adapter = new EFCoreAdapter<int>(provider);

                // Create enforcer without loading policy (tables might be empty)
                var model = DefaultModel.CreateFromFile(ModelPath);
                var enforcer = new Enforcer(model);
                enforcer.SetAdapter(adapter);

                // Add policies to in-memory model (not persisted yet)
                enforcer.AddPolicy("bob", "data2", "write");         // → casbin_policies
                enforcer.AddGroupingPolicy("bob", "developer");      // → casbin_groupings
                enforcer.AddNamedGroupingPolicy("g2", "developer", "role-contributor"); // → casbin_roles

                _output.WriteLine("Added policies to in-memory model:");
                _output.WriteLine("  p policy → should go to casbin_policies");
                _output.WriteLine("  g policy → should go to casbin_groupings");
                _output.WriteLine("  g2 policy → should go to casbin_roles");

                // Save to database
                await enforcer.SavePolicyAsync();
                _output.WriteLine("Called SavePolicyAsync()");

                // Verify distribution across schemas
                var policiesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.PoliciesSchema);
                var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.GroupingsSchema);
                var rolesCount = await _fixture.CountPoliciesInSchemaAsync(TransactionIntegrityTestFixture.RolesSchema);

                _output.WriteLine($"Schema distribution:");
                _output.WriteLine($"  casbin_policies: {policiesCount} policy");
                _output.WriteLine($"  casbin_groupings: {groupingsCount} policy");
                _output.WriteLine($"  casbin_roles: {rolesCount} policy");

                // CRITICAL ASSERTION: Policies should be distributed across all three schemas
                // If all policies end up in ONE schema, HasDefaultSchema() does NOT work with shared connections
                // and we NEED the SET search_path approach

                if (policiesCount == 1 && groupingsCount == 1 && rolesCount == 1)
                {
                    _output.WriteLine("✓✓✓ SHARED CONNECTION TEST PASSED!");
                    _output.WriteLine("HasDefaultSchema() correctly distributes policies even with shared connection");
                    _output.WriteLine("CONCLUSION: SET search_path approach is NOT necessary");
                }
                else
                {
                    _output.WriteLine("✗✗✗ SHARED CONNECTION TEST FAILED!");
                    _output.WriteLine($"Expected distribution: (1, 1, 1), Got: ({policiesCount}, {groupingsCount}, {rolesCount})");
                    _output.WriteLine("CONCLUSION: SET search_path approach IS necessary for shared connections");
                }

                Assert.Equal(1, policiesCount);
                Assert.Equal(1, groupingsCount);
                Assert.Equal(1, rolesCount);

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
    }
}
