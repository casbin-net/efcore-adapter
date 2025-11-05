using System;
using System.Data.Common;
using System.Threading.Tasks;
using Casbin.Persist.Adapter.EFCore.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Casbin.Persist.Adapter.EFCore.UnitTest.Integration
{
    /// <summary>
    /// Test fixture for transaction integrity tests using SQL Server LocalDB.
    /// Creates three separate schemas to simulate multi-context scenarios.
    /// </summary>
    public class TransactionIntegrityTestFixture : IAsyncLifetime
    {
        // Schema names for three-way context split
        public const string PoliciesSchema = "casbin_policies";
        public const string GroupingsSchema = "casbin_groupings";
        public const string RolesSchema = "casbin_roles";

        // Connection string to LocalDB
        public string ConnectionString { get; private set; }

        public TransactionIntegrityTestFixture()
        {
            // Use LocalDB for integration tests
            ConnectionString = "Server=(localdb)\\mssqllocaldb;Database=CasbinIntegrationTest;Trusted_Connection=True;MultipleActiveResultSets=true";
        }

        public async Task InitializeAsync()
        {
            // Ensure database exists
            await EnsureDatabaseExistsAsync();

            // Create schemas
            await CreateSchemasAsync();

            // Run migrations for all three schemas
            await RunMigrationsAsync();
        }

        public async Task DisposeAsync()
        {
            // Clean up test database
            await DropSchemasAsync();
        }

        private async Task EnsureDatabaseExistsAsync()
        {
            var builder = new SqlConnectionStringBuilder(ConnectionString);
            var databaseName = builder.InitialCatalog;

            // Connect to master database to create test database if needed
            builder.InitialCatalog = "master";
            var masterConnectionString = builder.ConnectionString;

            await using var connection = new SqlConnection(masterConnectionString);
            await connection.OpenAsync();

            // Check if database exists
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM sys.databases WHERE name = '{databaseName}'";
            var exists = (int)await cmd.ExecuteScalarAsync() > 0;

            if (!exists)
            {
                cmd.CommandText = $"CREATE DATABASE [{databaseName}]";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task CreateSchemasAsync()
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();

            // Create policies schema
            cmd.CommandText = $@"
                IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{PoliciesSchema}')
                BEGIN
                    EXEC('CREATE SCHEMA [{PoliciesSchema}]')
                END";
            await cmd.ExecuteNonQueryAsync();

            // Create groupings schema
            cmd.CommandText = $@"
                IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{GroupingsSchema}')
                BEGIN
                    EXEC('CREATE SCHEMA [{GroupingsSchema}]')
                END";
            await cmd.ExecuteNonQueryAsync();

            // Create roles schema
            cmd.CommandText = $@"
                IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{RolesSchema}')
                BEGIN
                    EXEC('CREATE SCHEMA [{RolesSchema}]')
                END";
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Runs migrations for all schemas. Public so tests can restore tables after dropping them.
        /// </summary>
        public async Task RunMigrationsAsync()
        {
            // Create contexts and run migrations for each schema
            var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();

            try
            {
                await RunMigrationForSchemaAsync(connection, PoliciesSchema);
                await RunMigrationForSchemaAsync(connection, GroupingsSchema);
                await RunMigrationForSchemaAsync(connection, RolesSchema);
            }
            finally
            {
                await connection.CloseAsync();
                await connection.DisposeAsync();
            }
        }

        private async Task RunMigrationForSchemaAsync(DbConnection connection, string schemaName)
        {
            var options = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseSqlServer(connection, b => b.MigrationsHistoryTable("__EFMigrationsHistory", schemaName))
                .Options;

            await using var context = new CasbinDbContext<int>(options, schemaName: schemaName);

            // Ensure schema and table are created
            await context.Database.ExecuteSqlRawAsync($@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'casbin_rule' AND schema_id = SCHEMA_ID('{schemaName}'))
                BEGIN
                    CREATE TABLE [{schemaName}].[casbin_rule] (
                        [id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        [ptype] nvarchar(254) NOT NULL,
                        [v0] nvarchar(254) NULL,
                        [v1] nvarchar(254) NULL,
                        [v2] nvarchar(254) NULL,
                        [v3] nvarchar(254) NULL,
                        [v4] nvarchar(254) NULL,
                        [v5] nvarchar(254) NULL
                    );
                    CREATE INDEX [IX_casbin_rule_ptype] ON [{schemaName}].[casbin_rule] ([ptype]);
                END");
        }

        private async Task DropSchemasAsync()
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();

            // Drop tables first, then schemas
            foreach (var schema in new[] { PoliciesSchema, GroupingsSchema, RolesSchema })
            {
                cmd.CommandText = $@"
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'casbin_rule' AND schema_id = SCHEMA_ID('{schema}'))
                    BEGIN
                        DROP TABLE [{schema}].[casbin_rule]
                    END";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = $@"
                    IF EXISTS (SELECT * FROM sys.schemas WHERE name = '{schema}')
                    BEGIN
                        DROP SCHEMA [{schema}]
                    END";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Clears all policies from all schemas. Call before each test.
        /// </summary>
        public async Task ClearAllPoliciesAsync()
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();

            foreach (var schema in new[] { PoliciesSchema, GroupingsSchema, RolesSchema })
            {
                cmd.CommandText = $"DELETE FROM [{schema}].[casbin_rule]";
                try
                {
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (SqlException)
                {
                    // Table might not exist yet, ignore
                }
            }
        }

        /// <summary>
        /// Counts policies of a specific type in a schema
        /// </summary>
        public async Task<int> CountPoliciesInSchemaAsync(string schemaName, string policyType = null)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            if (policyType == null)
            {
                cmd.CommandText = $"SELECT COUNT(*) FROM [{schemaName}].[casbin_rule]";
            }
            else
            {
                cmd.CommandText = $"SELECT COUNT(*) FROM [{schemaName}].[casbin_rule] WHERE [ptype] = @ptype";
                cmd.Parameters.AddWithValue("@ptype", policyType);
            }

            try
            {
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (SqlException)
            {
                // Table might not exist
                return 0;
            }
        }

        /// <summary>
        /// Inserts a policy directly into the database (for conflict simulation)
        /// </summary>
        public async Task InsertPolicyDirectlyAsync(string schemaName, string ptype, params string[] values)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $@"
                INSERT INTO [{schemaName}].[casbin_rule]
                ([ptype], [v0], [v1], [v2], [v3], [v4], [v5])
                VALUES (@ptype, @v0, @v1, @v2, @v3, @v4, @v5)";

            cmd.Parameters.AddWithValue("@ptype", ptype);
            for (int i = 0; i < 6; i++)
            {
                var value = i < values.Length ? values[i] : (object)DBNull.Value;
                cmd.Parameters.AddWithValue($"@v{i}", value);
            }

            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Drops a table in a schema (for failure simulation)
        /// </summary>
        public async Task DropTableAsync(string schemaName)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"DROP TABLE IF EXISTS [{schemaName}].[casbin_rule]";
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
