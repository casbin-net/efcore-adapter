using System;
using Microsoft.EntityFrameworkCore;

namespace Casbin.Persist.Adapter.EFCore.UnitTest.Fixtures
{
    /// <summary>
    /// Fixture for creating multi-context test scenarios with separate contexts for policies and groupings
    /// </summary>
    public class MultiContextProviderFixture : IDisposable
    {
        private bool _disposed;

        /// <summary>
        /// Creates a multi-context provider with separate contexts for policy and grouping rules.
        /// Both contexts share the same database but use different table names.
        /// </summary>
        /// <param name="testName">Unique name for this test to avoid database conflicts</param>
        /// <returns>A PolicyTypeContextProvider configured for testing</returns>
        public PolicyTypeContextProvider GetMultiContextProvider(string testName)
        {
            var dbName = $"MultiContext_{testName}.db";

            // Create policy context with "casbin_policy" table
            var policyOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseSqlite($"Data Source={dbName}")
                .Options;
            var policyContext = new CasbinDbContext<int>(policyOptions, schemaName: null, tableName: "casbin_policy");
            policyContext.Database.EnsureCreated();

            // Create grouping context with "casbin_grouping" table (same database)
            var groupingOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseSqlite($"Data Source={dbName}")
                .Options;
            var groupingContext = new CasbinDbContext<int>(groupingOptions, schemaName: null, tableName: "casbin_grouping");
            groupingContext.Database.EnsureCreated();

            return new PolicyTypeContextProvider(policyContext, groupingContext);
        }

        /// <summary>
        /// Gets separate contexts for direct verification in tests
        /// </summary>
        public (CasbinDbContext<int> policyContext, CasbinDbContext<int> groupingContext) GetSeparateContexts(string testName)
        {
            var dbName = $"MultiContext_{testName}.db";

            var policyOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseSqlite($"Data Source={dbName}")
                .Options;
            var policyContext = new CasbinDbContext<int>(policyOptions, schemaName: null, tableName: "casbin_policy");
            policyContext.Database.EnsureCreated();

            var groupingOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseSqlite($"Data Source={dbName}")
                .Options;
            var groupingContext = new CasbinDbContext<int>(groupingOptions, schemaName: null, tableName: "casbin_grouping");
            groupingContext.Database.EnsureCreated();

            return (policyContext, groupingContext);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Cleanup handled by test framework
                _disposed = true;
            }
        }
    }
}
