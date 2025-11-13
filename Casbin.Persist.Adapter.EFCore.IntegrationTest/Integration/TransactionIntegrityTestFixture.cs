using System;
using System.Threading.Tasks;
using Npgsql;
using Xunit;

namespace Casbin.Persist.Adapter.EFCore.UnitTest.Integration
{
    /// <summary>
    /// Test fixture for transaction integrity tests using PostgreSQL.
    /// Creates three separate schemas to simulate multi-context scenarios.
    ///
    /// Prerequisites:
    /// - PostgreSQL running on localhost:5432
    /// - Database "casbin_integration_test" must exist
    /// - Default credentials: postgres/postgres4all! (or update ConnectionString)
    /// </summary>
    public class TransactionIntegrityTestFixture : IAsyncLifetime
    {
        // Schema names for three-way context split
        public const string PoliciesSchema = "casbin_policies";
        public const string GroupingsSchema = "casbin_groupings";
        public const string RolesSchema = "casbin_roles";

        // Connection string to local PostgreSQL
        public string ConnectionString { get; private set; }

        public TransactionIntegrityTestFixture()
        {
            // Use local PostgreSQL for integration tests
            // Database must exist before running tests
            ConnectionString = "Host=localhost;Database=casbin_integration_test;Username=postgres;Password=postgres4all!";
        }

        public async Task InitializeAsync()
        {
            try
            {
                // Create schemas
                await CreateSchemasAsync();

                // Run migrations for all three schemas
                await RunMigrationsAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to initialize TransactionIntegrityTestFixture. " +
                    $"Ensure PostgreSQL is running and database 'casbin_integration_test' exists. " +
                    $"Connection string: {ConnectionString}", ex);
            }
        }

        public async Task DisposeAsync()
        {
            // Clean up test schemas
            // TEMPORARILY DISABLED: Comment out to leave tables for inspection
            // await DropSchemasAsync();
            await Task.CompletedTask;
        }

        private async Task CreateSchemasAsync()
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();

            // Create policies schema
            cmd.CommandText = $"CREATE SCHEMA IF NOT EXISTS {PoliciesSchema}";
            await cmd.ExecuteNonQueryAsync();

            // Create groupings schema
            cmd.CommandText = $"CREATE SCHEMA IF NOT EXISTS {GroupingsSchema}";
            await cmd.ExecuteNonQueryAsync();

            // Create roles schema
            cmd.CommandText = $"CREATE SCHEMA IF NOT EXISTS {RolesSchema}";
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Runs migrations for all schemas. Public so tests can restore tables after dropping them.
        /// </summary>
        public async Task RunMigrationsAsync()
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();

            foreach (var schemaName in new[] { PoliciesSchema, GroupingsSchema, RolesSchema })
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = $@"
                    CREATE TABLE IF NOT EXISTS {schemaName}.casbin_rule (
                        id SERIAL PRIMARY KEY,
                        ptype VARCHAR(254) NOT NULL,
                        v0 VARCHAR(254),
                        v1 VARCHAR(254),
                        v2 VARCHAR(254),
                        v3 VARCHAR(254),
                        v4 VARCHAR(254),
                        v5 VARCHAR(254),
                        v6 VARCHAR(254),
                        v7 VARCHAR(254),
                        v8 VARCHAR(254),
                        v9 VARCHAR(254),
                        v10 VARCHAR(254),
                        v11 VARCHAR(254),
                        v12 VARCHAR(254),
                        v13 VARCHAR(254)
                    );
                    CREATE INDEX IF NOT EXISTS ix_casbin_rule_ptype ON {schemaName}.casbin_rule (ptype);
                ";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task DropSchemasAsync()
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();

            // Drop tables first, then schemas
            foreach (var schema in new[] { PoliciesSchema, GroupingsSchema, RolesSchema })
            {
                cmd.CommandText = $"DROP TABLE IF EXISTS {schema}.casbin_rule CASCADE";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = $"DROP SCHEMA IF EXISTS {schema} CASCADE";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Clears all policies from all schemas. Call before each test.
        /// </summary>
        public async Task ClearAllPoliciesAsync()
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();

            foreach (var schema in new[] { PoliciesSchema, GroupingsSchema, RolesSchema })
            {
                cmd.CommandText = $"DELETE FROM {schema}.casbin_rule";
                try
                {
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (NpgsqlException)
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
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            if (policyType == null)
            {
                cmd.CommandText = $"SELECT COUNT(*) FROM {schemaName}.casbin_rule";
            }
            else
            {
                cmd.CommandText = $"SELECT COUNT(*) FROM {schemaName}.casbin_rule WHERE ptype = @ptype";
                cmd.Parameters.AddWithValue("@ptype", policyType);
            }

            try
            {
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (NpgsqlException)
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
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $@"
                INSERT INTO {schemaName}.casbin_rule
                (ptype, v0, v1, v2, v3, v4, v5)
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
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"DROP TABLE IF EXISTS {schemaName}.casbin_rule CASCADE";
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
