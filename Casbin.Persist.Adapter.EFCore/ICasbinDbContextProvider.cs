using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Casbin.Persist.Adapter.EFCore
{
    /// <summary>
    /// Provides DbContext instances for different policy types, enabling multi-context scenarios
    /// where different policy types can be stored in separate schemas, tables, or databases.
    /// </summary>
    /// <typeparam name="TKey">The type of the primary key</typeparam>
    public interface ICasbinDbContextProvider<TKey> where TKey : IEquatable<TKey>
    {
        /// <summary>
        /// Gets the DbContext that should handle the specified policy type.
        /// </summary>
        /// <param name="policyType">The policy type identifier (e.g., "p", "p2", "g", "g2")</param>
        /// <returns>The DbContext instance responsible for this policy type</returns>
        DbContext GetContextForPolicyType(string policyType);

        /// <summary>
        /// Gets all unique DbContext instances managed by this provider.
        /// Used for operations that need to coordinate across all contexts (e.g., SavePolicy, LoadPolicy).
        /// </summary>
        /// <returns>An enumerable of all distinct DbContext instances</returns>
        IEnumerable<DbContext> GetAllContexts();
    }
}
