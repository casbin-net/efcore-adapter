using System;
using System.Collections.Generic;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Casbin.Persist.Adapter.EFCore
{
    /// <summary>
    /// Context provider that resolves a single DbContext from IServiceProvider (for DI scenarios).
    /// </summary>
    /// <typeparam name="TKey">The type of the primary key</typeparam>
    /// <typeparam name="TDbContext">The type of DbContext to resolve</typeparam>
    internal class ServiceProviderContextProvider<TKey, TDbContext> : ICasbinDbContextProvider<TKey>
        where TKey : IEquatable<TKey>
        where TDbContext : DbContext
    {
        private readonly IServiceProvider _serviceProvider;
        private TDbContext _cachedContext;

        public ServiceProviderContextProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public DbContext GetContextForPolicyType(string policyType)
        {
            return GetOrResolveContext();
        }

        public IEnumerable<DbContext> GetAllContexts()
        {
            yield return GetOrResolveContext();
        }

        public DbConnection GetSharedConnection()
        {
            // Single context - return its connection for shared transaction support
            return GetOrResolveContext().Database.GetDbConnection();
        }

        private TDbContext GetOrResolveContext()
        {
            if (_cachedContext != null)
            {
                return _cachedContext;
            }

            _cachedContext = _serviceProvider.GetService(typeof(TDbContext)) as TDbContext
                ?? throw new InvalidOperationException($"Unable to resolve service for type '{typeof(TDbContext)}' from IServiceProvider.");

            return _cachedContext;
        }
    }
}
