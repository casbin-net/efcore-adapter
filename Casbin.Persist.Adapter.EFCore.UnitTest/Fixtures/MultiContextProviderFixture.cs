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
        /// Uses separate database files with the same table name for proper isolation.
        /// This approach avoids SQLite transaction limitations across tables.
        /// </summary>
        /// <param name="testName">Unique name for this test to avoid database conflicts</param>
        /// <returns>A PolicyTypeContextProvider configured for testing</returns>
        public PolicyTypeContextProvider GetMultiContextProvider(string testName)
        {
            // Use separate database files for proper isolation
            var policyDbName = $"MultiContext_{testName}_policy.db";
            var groupingDbName = $"MultiContext_{testName}_grouping.db";

            // Create policy context with its own database and default table name
            var policyOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseSqlite($"Data Source={policyDbName}")
                .Options;
            var policyContext = new CasbinDbContext<int>(policyOptions);
            policyContext.Database.EnsureCreated();

            // Create grouping context with its own database and default table name
            var groupingOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseSqlite($"Data Source={groupingDbName}")
                .Options;
            var groupingContext = new CasbinDbContext<int>(groupingOptions);
            groupingContext.Database.EnsureCreated();

            return new PolicyTypeContextProvider(policyContext, groupingContext);
        }

        /// <summary>
        /// Gets separate contexts for direct verification in tests.
        /// Returns NEW context instances pointing to the same databases as the provider.
        /// </summary>
        public (CasbinDbContext<int> policyContext, CasbinDbContext<int> groupingContext) GetSeparateContexts(string testName)
        {
            // Use same database file names as GetMultiContextProvider
            var policyDbName = $"MultiContext_{testName}_policy.db";
            var groupingDbName = $"MultiContext_{testName}_grouping.db";

            // Create new context instances that point to the same database files
            var policyOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseSqlite($"Data Source={policyDbName}")
                .Options;
            var policyContext = new CasbinDbContext<int>(policyOptions);
            policyContext.Database.EnsureCreated();

            var groupingOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseSqlite($"Data Source={groupingDbName}")
                .Options;
            var groupingContext = new CasbinDbContext<int>(groupingOptions);
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
