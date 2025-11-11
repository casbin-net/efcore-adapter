using System;
using System.Collections.Generic;
using System.Data.Common;
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

        /// <summary>
        /// Gets the shared DbConnection if all contexts use the same physical connection.
        /// Returns null if contexts use separate connections.
        /// </summary>
        /// <remarks>
        /// When non-null, the adapter starts transactions at the connection level
        /// (connection.BeginTransaction()) rather than context level, which is required
        /// for proper savepoint handling in PostgreSQL and other databases that require
        /// explicit transaction blocks before creating savepoints.
        ///
        /// Return null for scenarios where contexts use separate physical connections
        /// (e.g., separate SQLite database files), in which case the adapter will use
        /// separate transactions for each context.
        /// </remarks>
        /// <returns>The shared DbConnection, or null if contexts use separate connections</returns>
        DbConnection? GetSharedConnection();
    }
}
