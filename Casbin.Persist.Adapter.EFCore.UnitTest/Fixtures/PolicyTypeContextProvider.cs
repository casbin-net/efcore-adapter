using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

#nullable enable

namespace Casbin.Persist.Adapter.EFCore.UnitTest.Fixtures
{
    /// <summary>
    /// Test context provider that routes policy types (p, p2, etc.) to one context
    /// and grouping types (g, g2, etc.) to another context.
    /// </summary>
    public class PolicyTypeContextProvider : ICasbinDbContextProvider<int>
    {
        private readonly CasbinDbContext<int> _policyContext;
        private readonly CasbinDbContext<int> _groupingContext;

        public PolicyTypeContextProvider(
            CasbinDbContext<int> policyContext,
            CasbinDbContext<int> groupingContext)
        {
            _policyContext = policyContext ?? throw new ArgumentNullException(nameof(policyContext));
            _groupingContext = groupingContext ?? throw new ArgumentNullException(nameof(groupingContext));
        }

        public DbContext GetContextForPolicyType(string policyType)
        {
            if (string.IsNullOrEmpty(policyType))
            {
                return _policyContext;
            }

            // Route 'p' types (p, p2, p3, etc.) to policy context
            // Route 'g' types (g, g2, g3, etc.) to grouping context
            return policyType.StartsWith("p", StringComparison.OrdinalIgnoreCase)
                ? _policyContext
                : _groupingContext;
        }

        public IEnumerable<DbContext> GetAllContexts()
        {
            return new DbContext[] { _policyContext, _groupingContext };
        }

        public System.Data.Common.DbConnection? GetSharedConnection()
        {
            // Return null since this provider uses separate SQLite database files
            // (each context has its own connection)
            return null;
        }
    }
}
