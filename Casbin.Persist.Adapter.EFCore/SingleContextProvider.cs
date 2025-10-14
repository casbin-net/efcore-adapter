using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Casbin.Persist.Adapter.EFCore
{
    /// <summary>
    /// Default context provider that uses a single DbContext for all policy types.
    /// This maintains backward compatibility with the original single-context behavior.
    /// </summary>
    /// <typeparam name="TKey">The type of the primary key</typeparam>
    public class SingleContextProvider<TKey> : ICasbinDbContextProvider<TKey>
        where TKey : IEquatable<TKey>
    {
        private readonly DbContext _context;

        /// <summary>
        /// Creates a new instance of SingleContextProvider with the specified context.
        /// </summary>
        /// <param name="context">The DbContext to use for all policy types</param>
        /// <exception cref="ArgumentNullException">Thrown when context is null</exception>
        public SingleContextProvider(DbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Returns the single context for any policy type.
        /// </summary>
        /// <param name="policyType">The policy type (ignored in this implementation)</param>
        /// <returns>The single DbContext instance</returns>
        public DbContext GetContextForPolicyType(string policyType)
        {
            return _context;
        }

        /// <summary>
        /// Returns a collection containing only the single context.
        /// </summary>
        /// <returns>An enumerable containing the single DbContext</returns>
        public IEnumerable<DbContext> GetAllContexts()
        {
            return new[] { _context };
        }
    }
}
