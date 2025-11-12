using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin;
using Casbin.Persist.Adapter.EFCore.Entities;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Xunit;

#nullable enable

namespace Casbin.Persist.Adapter.EFCore.UnitTest.Integration
{
    /// <summary>
    /// Integration tests verifying transaction integrity guarantees for multi-context scenarios.
    /// These tests prove that shared DbConnection objects enable atomic transactions across multiple contexts.
    ///
    /// IMPORTANT: These tests are excluded from CI/CD via the "Integration" trait.
    /// Run locally with: dotnet test --filter "Category=Integration"
    /// </summary>
    [Trait("Category", "Integration")]
    [Collection("IntegrationTests")]
    public class TransactionIntegrityTests : IClassFixture<TransactionIntegrityTestFixture>, IAsyncLifetime
    {
        private readonly TransactionIntegrityTestFixture _fixture;
        private const string ModelPath = "examples/multi_context_model.conf";

        public TransactionIntegrityTests(TransactionIntegrityTestFixture fixture)
        {
            _fixture = fixture;
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

        #region Helper Methods

        /// <summary>
        /// Creates a three-way context provider routing policies to different schemas:
        /// - p, p2, p3... → policies schema
        /// - g → groupings schema
        /// - g2, g3, g4... → roles schema
        /// </summary>
        private class ThreeWayPolicyTypeProvider : ICasbinDbContextProvider<int>
        {
            private readonly DbContext _policyContext;
            private readonly DbContext _groupingContext;
            private readonly DbContext _roleContext;
            private readonly System.Data.Common.DbConnection? _sharedConnection;

            public ThreeWayPolicyTypeProvider(
                DbContext policyContext,
                DbContext groupingContext,
                DbContext roleContext,
                System.Data.Common.DbConnection? sharedConnection)
            {
                _policyContext = policyContext ?? throw new ArgumentNullException(nameof(policyContext));
                _groupingContext = groupingContext ?? throw new ArgumentNullException(nameof(groupingContext));
                _roleContext = roleContext ?? throw new ArgumentNullException(nameof(roleContext));
                _sharedConnection = sharedConnection;
            }

            public DbContext GetContextForPolicyType(string policyType)
            {
                if (string.IsNullOrEmpty(policyType))
                    return _policyContext;

                // Route p policies to policy context
                if (policyType.StartsWith("p", StringComparison.OrdinalIgnoreCase))
                    return _policyContext;

                // Route g2+ to role context
                if (policyType.StartsWith("g2", StringComparison.OrdinalIgnoreCase) ||
                    policyType.StartsWith("g3", StringComparison.OrdinalIgnoreCase) ||
                    policyType.StartsWith("g4", StringComparison.OrdinalIgnoreCase))
                    return _roleContext;

                // Route g to grouping context
                return _groupingContext;
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
        /// Creates an enforcer with three contexts sharing the same DbConnection
        /// </summary>
        private async Task<(Enforcer enforcer, NpgsqlConnection connection)> CreateEnforcerWithSharedConnectionAsync()
        {
            // Create ONE shared connection
            var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.OpenAsync();

            // Create three contexts sharing the same connection
            var policyContext = CreateContext(connection, TransactionIntegrityTestFixture.PoliciesSchema);
            var groupingContext = CreateContext(connection, TransactionIntegrityTestFixture.GroupingsSchema);
            var roleContext = CreateContext(connection, TransactionIntegrityTestFixture.RolesSchema);

            // Create provider routing policy types to appropriate contexts
            var provider = new ThreeWayPolicyTypeProvider(policyContext, groupingContext, roleContext, connection);

            // Create adapter and enforcer
            var adapter = new EFCoreAdapter<int>(provider);
            var enforcer = new Enforcer(ModelPath, adapter);

            await enforcer.LoadPolicyAsync();

            return (enforcer, connection);
        }

        /// <summary>
        /// Creates an enforcer with three contexts using SEPARATE DbConnections (same connection string)
        /// This is used to demonstrate non-atomic behavior when connections are not shared.
        /// </summary>
        private async Task<Enforcer> CreateEnforcerWithSeparateConnectionsAsync()
        {
            // Create THREE separate connections with same connection string
            var policyConnection = new NpgsqlConnection(_fixture.ConnectionString);
            var groupingConnection = new NpgsqlConnection(_fixture.ConnectionString);
            var roleConnection = new NpgsqlConnection(_fixture.ConnectionString);

            await policyConnection.OpenAsync();
            await groupingConnection.OpenAsync();
            await roleConnection.OpenAsync();

            // Create three contexts with different connection objects
            var policyContext = CreateContext(policyConnection, TransactionIntegrityTestFixture.PoliciesSchema);
            var groupingContext = CreateContext(groupingConnection, TransactionIntegrityTestFixture.GroupingsSchema);
            var roleContext = CreateContext(roleConnection, TransactionIntegrityTestFixture.RolesSchema);

            // Create provider routing policy types to appropriate contexts
            // Pass null for shared connection since these contexts use separate connections
            var provider = new ThreeWayPolicyTypeProvider(policyContext, groupingContext, roleContext, null);

            // Create adapter and enforcer
            var adapter = new EFCoreAdapter<int>(provider);
            var enforcer = new Enforcer(ModelPath, adapter);

            await enforcer.LoadPolicyAsync();

            return enforcer;
        }

        private CasbinDbContext<int> CreateContext(NpgsqlConnection connection, string schemaName)
        {
            var options = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseNpgsql(connection, b => b.MigrationsHistoryTable("__EFMigrationsHistory", schemaName))
                .Options;

            // Return appropriate derived context based on schema name
            if (schemaName == TransactionIntegrityTestFixture.PoliciesSchema)
                return new TestCasbinDbContext1(options, schemaName, "casbin_rule");
            else if (schemaName == TransactionIntegrityTestFixture.GroupingsSchema)
                return new TestCasbinDbContext2(options, schemaName, "casbin_rule");
            else if (schemaName == TransactionIntegrityTestFixture.RolesSchema)
                return new TestCasbinDbContext3(options, schemaName, "casbin_rule");
            else
                throw new ArgumentException($"Unknown schema name: {schemaName}", nameof(schemaName));
        }

        #endregion

        #region Test 1: Atomicity - Happy Path

        [Fact]
        public async Task SavePolicy_WithSharedConnection_ShouldWriteToAllContextsAtomically()
        {
            // Arrange
            var (enforcer, connection) = await CreateEnforcerWithSharedConnectionAsync();

            try
            {
                // Add policies that will route to different contexts
                await enforcer.AddPolicyAsync("alice", "data1", "read");         // → policies schema (p)
                await enforcer.AddGroupingPolicyAsync("alice", "admin");         // → groupings schema (g)
                await enforcer.AddNamedGroupingPolicyAsync("g2", "admin", "superuser"); // → roles schema (g2)

                // Act
                await enforcer.SavePolicyAsync();

                // Assert - Verify each schema has exactly the expected policies
                var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.PoliciesSchema, "p");
                var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.GroupingsSchema, "g");
                var rolesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.RolesSchema, "g2");

                Assert.Equal(1, policiesCount);
                Assert.Equal(1, groupingsCount);
                Assert.Equal(1, rolesCount);
            }
            finally
            {
                await connection.CloseAsync();
                await connection.DisposeAsync();
            }
        }

        #endregion

        #region Test 2: Connection Sharing Verification

        [Fact]
        public async Task MultiContextSetup_WithSharedConnection_ShouldShareSamePhysicalConnection()
        {
            // Arrange
            var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.OpenAsync();

            try
            {
                var policyContext = CreateContext(connection, TransactionIntegrityTestFixture.PoliciesSchema);
                var groupingContext = CreateContext(connection, TransactionIntegrityTestFixture.GroupingsSchema);
                var roleContext = CreateContext(connection, TransactionIntegrityTestFixture.RolesSchema);

                // Act & Assert - Verify reference equality (not just connection string equality)
                var policyConn = policyContext.Database.GetDbConnection();
                var groupingConn = groupingContext.Database.GetDbConnection();
                var roleConn = roleContext.Database.GetDbConnection();

                Assert.Same(connection, policyConn);
                Assert.Same(connection, groupingConn);
                Assert.Same(connection, roleConn);
                Assert.Same(policyConn, groupingConn);
                Assert.Same(groupingConn, roleConn);
            }
            finally
            {
                await connection.CloseAsync();
                await connection.DisposeAsync();
            }
        }

        #endregion

        #region Test 3: Rollback - Missing Table (CRITICAL TEST)

        [Fact]
        public async Task SavePolicy_WhenTableDroppedInOneContext_ShouldRollbackAllContexts()
        {
            // Arrange
            var (enforcer, connection) = await CreateEnforcerWithSharedConnectionAsync();

            try
            {
                // Disable AutoSave so policies stay in-memory until SavePolicyAsync() is called
                enforcer.EnableAutoSave(false);

                // Add policies to all contexts (in memory only, AutoSave is OFF)
                await enforcer.AddPolicyAsync("alice", "data1", "read");           // → policies schema
                await enforcer.AddGroupingPolicyAsync("alice", "admin");            // → groupings schema
                await enforcer.AddNamedGroupingPolicyAsync("g2", "admin", "superuser"); // → roles schema

                // Drop table in roles schema AFTER policies are in memory but BEFORE SavePolicy
                // This simulates a catastrophic failure scenario where database schema is inconsistent
                await _fixture.DropTableAsync(TransactionIntegrityTestFixture.RolesSchema);

                Exception? caughtException = null;

                // Act - Try to save, should throw due to missing table in roles schema
                try
                {
                    await enforcer.SavePolicyAsync();
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }

                // Assert - Verify exception was thrown
                Assert.NotNull(caughtException);

                // Recreate table for verification queries
                await _fixture.RunMigrationsAsync();

                // CRITICAL ASSERTION - Verify ZERO policies in all contexts (rollback successful)
                // This proves that when one context fails, ALL contexts roll back atomically
                var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.PoliciesSchema, "p");
                var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.GroupingsSchema, "g");
                var rolesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.RolesSchema, "g2");

                // Verify atomicity: All contexts rolled back (no partial commits)
                Assert.Equal(0, policiesCount);   // Should be 0 (rolled back)
                Assert.Equal(0, groupingsCount);  // Should be 0 (rolled back)
                Assert.Equal(0, rolesCount);      // Should be 0 (rolled back)

                // If we got here, atomicity is PROVEN!
            }
            finally
            {
                await connection.CloseAsync();
                await connection.DisposeAsync();

                // Restore table for subsequent tests
                await _fixture.RunMigrationsAsync();
            }
        }

        #endregion

        #region Test 4: Rollback - Missing Table

        [Fact]
        public async Task SavePolicy_WhenTableMissingInOneContext_ShouldRollbackAllContexts()
        {
            // Arrange
            var (enforcer, connection) = await CreateEnforcerWithSharedConnectionAsync();

            try
            {
                // Disable AutoSave so policies stay in-memory until SavePolicyAsync() is called
                enforcer.EnableAutoSave(false);

                // Add policies to all contexts
                await enforcer.AddPolicyAsync("alice", "data1", "read");
                await enforcer.AddGroupingPolicyAsync("alice", "admin");
                await enforcer.AddNamedGroupingPolicyAsync("g2", "admin", "superuser");

                // Drop table in roles schema AFTER enforcer is created
                await _fixture.DropTableAsync(TransactionIntegrityTestFixture.RolesSchema);

                Exception? caughtException = null;

                // Act - Try to save, should throw due to missing table
                try
                {
                    await enforcer.SavePolicyAsync();
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }

                // Assert - Verify exception was thrown
                Assert.NotNull(caughtException);

                // Recreate table for verification queries
                await _fixture.RunMigrationsAsync();

                // CRITICAL ASSERTION - Verify ZERO policies in all contexts (rollback successful)
                var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.PoliciesSchema, "p");
                var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.GroupingsSchema, "g");
                var rolesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.RolesSchema, "g2");

                Assert.Equal(0, policiesCount);
                Assert.Equal(0, groupingsCount);
                Assert.Equal(0, rolesCount);
            }
            finally
            {
                await connection.CloseAsync();
                await connection.DisposeAsync();

                // Restore table for subsequent tests
                await _fixture.RunMigrationsAsync();
            }
        }

        #endregion

        #region Test 5: Consistency Verification

        [Fact]
        public async Task MultipleSaveOperations_WithSharedConnection_ShouldMaintainDataConsistency()
        {
            // Arrange
            var (enforcer, connection) = await CreateEnforcerWithSharedConnectionAsync();

            try
            {
                // Act - Perform multiple incremental saves
                // Save 1
                await enforcer.AddPolicyAsync("alice", "data1", "read");
                await enforcer.AddGroupingPolicyAsync("alice", "admin");
                await enforcer.SavePolicyAsync();

                // Save 2
                await enforcer.AddPolicyAsync("bob", "data2", "write");
                await enforcer.AddGroupingPolicyAsync("bob", "user");
                await enforcer.SavePolicyAsync();

                // Save 3
                await enforcer.AddPolicyAsync("charlie", "data3", "read");
                await enforcer.AddGroupingPolicyAsync("charlie", "user");
                await enforcer.SavePolicyAsync();

                // Assert - Verify all 6 policies present (3 p policies, 3 g policies)
                var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.PoliciesSchema, "p");
                var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.GroupingsSchema, "g");

                Assert.Equal(3, policiesCount);
                Assert.Equal(3, groupingsCount);

                // Verify all policies are enforced correctly
                Assert.True(await enforcer.EnforceAsync("alice", "data1", "read"));
                Assert.True(await enforcer.EnforceAsync("bob", "data2", "write"));
                Assert.True(await enforcer.EnforceAsync("charlie", "data3", "read"));
            }
            finally
            {
                await connection.CloseAsync();
                await connection.DisposeAsync();
            }
        }

        #endregion

        #region Test 6: Non-Atomic Behavior Without Shared Connection

        [Fact]
        public async Task SavePolicy_WithSeparateConnections_ShouldNotBeAtomic()
        {
            // Arrange - Create enforcer with SEPARATE connection objects
            var enforcer = await CreateEnforcerWithSeparateConnectionsAsync();

            // Add policies to all contexts
            await enforcer.AddPolicyAsync("alice", "data1", "read");
            await enforcer.AddGroupingPolicyAsync("alice", "admin");
            await enforcer.AddNamedGroupingPolicyAsync("g2", "admin", "superuser");

            // Drop table in roles schema to force failure
            await _fixture.DropTableAsync(TransactionIntegrityTestFixture.RolesSchema);

            Exception? caughtException = null;

            // Act - Try to save, should throw due to missing table in roles schema
            try
            {
                await enforcer.SavePolicyAsync();
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            // Assert - Verify exception was thrown
            Assert.NotNull(caughtException);

            // Recreate table for verification queries
            await _fixture.RunMigrationsAsync();

            // CRITICAL ASSERTION - Verify policies WERE written to functioning contexts (NOT atomic!)
            var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
                TransactionIntegrityTestFixture.PoliciesSchema, "p");
            var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
                TransactionIntegrityTestFixture.GroupingsSchema, "g");
            var rolesCount = await _fixture.CountPoliciesInSchemaAsync(
                TransactionIntegrityTestFixture.RolesSchema, "g2");

            // This test DOCUMENTS the non-atomic behavior without shared connections
            // Policies and groupings were committed despite roles context failure
            Assert.Equal(1, policiesCount);   // Written (NOT rolled back)
            Assert.Equal(1, groupingsCount);  // Written (NOT rolled back)
            Assert.Equal(0, rolesCount);      // Failed to write (table dropped)

            // This proves that connection string matching alone is INSUFFICIENT for atomicity
            // Must use shared DbConnection OBJECT for atomic transactions
        }

        #endregion

        #region Test 7: Casbin In-Memory vs Database State

        [Fact]
        public async Task SavePolicy_ShouldReflectDatabaseStateNotCasbinMemory()
        {
            // Arrange - Create first enforcer and save policies
            var (enforcer1, connection1) = await CreateEnforcerWithSharedConnectionAsync();

            try
            {
                await enforcer1.AddPolicyAsync("alice", "data1", "read");
                await enforcer1.AddGroupingPolicyAsync("alice", "admin");
                await enforcer1.SavePolicyAsync();
            }
            finally
            {
                await connection1.CloseAsync();
                await connection1.DisposeAsync();
            }

            // Create second enforcer - loads existing policies from database
            var (enforcer2, connection2) = await CreateEnforcerWithSharedConnectionAsync();

            try
            {
                // Act - Try to add same policies again
                var addedPolicy = await enforcer2.AddPolicyAsync("alice", "data1", "read");
                var addedGrouping = await enforcer2.AddGroupingPolicyAsync("alice", "admin");

                // Assert - Casbin's in-memory check should prevent duplicates
                Assert.False(addedPolicy);
                Assert.False(addedGrouping);

                // Verify database unchanged (validates tests check database, not just Casbin memory)
                var policiesCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.PoliciesSchema, "p");
                var groupingsCount = await _fixture.CountPoliciesInSchemaAsync(
                    TransactionIntegrityTestFixture.GroupingsSchema, "g");

                Assert.Equal(1, policiesCount);
                Assert.Equal(1, groupingsCount);
            }
            finally
            {
                await connection2.CloseAsync();
                await connection2.DisposeAsync();
            }
        }

        #endregion
    }
}
