using Casbin.Model;
using System.Collections.Generic;
using System.Linq;
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Threading.Tasks;
using Casbin.Persist.Adapter.EFCore.Extensions;
using Casbin.Persist.Adapter.EFCore.Entities;

// ReSharper disable InconsistentNaming
// ReSharper disable MemberCanBeProtected.Global
// ReSharper disable MemberCanBePrivate.Global

namespace Casbin.Persist.Adapter.EFCore
{
    public class EFCoreAdapter<TKey> : EFCoreAdapter<TKey, EFCorePersistPolicy<TKey>> where TKey : IEquatable<TKey>
    {
        public EFCoreAdapter(CasbinDbContext<TKey> context) : base(context)
        {

        }

        public EFCoreAdapter(ICasbinDbContextProvider<TKey> contextProvider) : base(contextProvider)
        {

        }
    }

    public class EFCoreAdapter<TKey, TPersistPolicy> : EFCoreAdapter<TKey, TPersistPolicy, CasbinDbContext<TKey>>
        where TPersistPolicy : class, IEFCorePersistPolicy<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        // For dependency injection to use.
        // ReSharper disable once SuggestBaseTypeForParameter
        public EFCoreAdapter(CasbinDbContext<TKey> context) : base(context)
        {

        }

        public EFCoreAdapter(ICasbinDbContextProvider<TKey> contextProvider) : base(contextProvider)
        {

        }
    }

    /// <summary>
    /// Entity Framework Core adapter for Casbin authorization library.
    /// Supports both single-context and multi-context scenarios for policy storage.
    /// </summary>
    /// <remarks>
    /// <para><strong>Performance Considerations:</strong></para>
    /// <para>
    /// The adapter caches DbSet instances per (DbContext, policyType) combination in an
    /// internal dictionary for performance. This cache grows to at most (N contexts × M policy types)
    /// entries, typically 2-8 entries in practice. Memory overhead is negligible (~224 bytes typical,
    /// ~3.5 KB worst-case). The cache lifetime matches the adapter instance lifetime.
    /// </para>
    /// <para><strong>Lifecycle:</strong></para>
    /// <para>
    /// In multi-context scenarios, ensure DbContext instances live at least as long as the
    /// adapter instance. Typical usage patterns (singleton DI registration or test fixtures)
    /// naturally satisfy this requirement.
    /// </para>
    /// </remarks>
    /// <typeparam name="TKey">The type of the policy identifier (e.g., int, Guid, string)</typeparam>
    /// <typeparam name="TPersistPolicy">The entity type for persisting policies</typeparam>
    /// <typeparam name="TDbContext">The DbContext type</typeparam>
    public partial class EFCoreAdapter<TKey, TPersistPolicy, TDbContext> : IAdapter, IFilteredAdapter
        where TDbContext : DbContext
        where TPersistPolicy : class, IEFCorePersistPolicy<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        private DbSet<TPersistPolicy> _persistPolicies;
        private readonly ICasbinDbContextProvider<TKey> _contextProvider;
        private readonly Dictionary<(DbContext context, string policyType), DbSet<TPersistPolicy>> _persistPoliciesByContext;

        protected TDbContext DbContext { get; }
        protected DbSet<TPersistPolicy> PersistPolicies => _persistPolicies ??= GetCasbinRuleDbSet(DbContext);

        /// <summary>
        /// Creates adapter with single context (backward compatible)
        /// </summary>
        public EFCoreAdapter(TDbContext context)
        {
            DbContext = context ?? throw new ArgumentNullException(nameof(context));
            _contextProvider = new SingleContextProvider<TKey>(context);
            _persistPoliciesByContext = new Dictionary<(DbContext context, string policyType), DbSet<TPersistPolicy>>();
        }

        /// <summary>
        /// Creates adapter with custom context provider for multi-context scenarios
        /// </summary>
        public EFCoreAdapter(ICasbinDbContextProvider<TKey> contextProvider)
        {
            _contextProvider = contextProvider ?? throw new ArgumentNullException(nameof(contextProvider));
            _persistPoliciesByContext = new Dictionary<(DbContext context, string policyType), DbSet<TPersistPolicy>>();
            DbContext = null; // Multi-context mode - DbContext not applicable
        }

        #region Load policy

        public virtual void LoadPolicy(IPolicyStore store)
        {
            var allPolicies = new List<TPersistPolicy>();

            // Load from each unique context
            foreach (var context in _contextProvider.GetAllContexts().Distinct())
            {
                var dbSet = GetCasbinRuleDbSet(context, null);
                var policies = dbSet.AsNoTracking().ToList();
                allPolicies.AddRange(policies);
            }

            var filteredPolicies = OnLoadPolicy(store, allPolicies.AsQueryable());
            store.LoadPolicyFromPersistPolicy(filteredPolicies.ToList());
            IsFiltered = false;
        }

        public virtual async Task LoadPolicyAsync(IPolicyStore store)
        {
            var allPolicies = new List<TPersistPolicy>();

            // Load from each unique context
            foreach (var context in _contextProvider.GetAllContexts().Distinct())
            {
                var dbSet = GetCasbinRuleDbSet(context, null);
                var policies = await dbSet.AsNoTracking().ToListAsync();
                allPolicies.AddRange(policies);
            }

            var filteredPolicies = OnLoadPolicy(store, allPolicies.AsQueryable());
            store.LoadPolicyFromPersistPolicy(filteredPolicies.ToList());
            IsFiltered = false;
        }

        #endregion

        #region Save policy

        public virtual void SavePolicy(IPolicyStore store)
        {
            var persistPolicies = new List<TPersistPolicy>();
            persistPolicies.ReadPolicyFromCasbinModel(store);

            if (persistPolicies.Count is 0)
            {
                return;
            }

            // Group policies by their target context
            var policiesByContext = persistPolicies
                .GroupBy(p => _contextProvider.GetContextForPolicyType(p.Type))
                .ToList();

            var contexts = _contextProvider.GetAllContexts().Distinct().ToList();

            // Check if we can use a shared transaction (all contexts use same connection)
            if (contexts.Count == 1 || CanShareTransaction(contexts))
            {
                // Single context or shared connection - use single transaction
                SavePolicyWithSharedTransaction(store, contexts, policiesByContext);
            }
            else
            {
                // Multiple separate databases - use individual transactions per context
                SavePolicyWithIndividualTransactions(store, contexts, policiesByContext);
            }
        }

        private void SavePolicyWithSharedTransaction(IPolicyStore store, List<DbContext> contexts,
            List<IGrouping<DbContext, TPersistPolicy>> policiesByContext)
        {
            var sharedConnection = _contextProvider?.GetSharedConnection();

            if (sharedConnection != null)
            {
                // Use connection-level transaction (required for PostgreSQL savepoint handling)
                if (sharedConnection.State != System.Data.ConnectionState.Open)
                {
                    sharedConnection.Open();
                }

                using var transaction = sharedConnection.BeginTransaction();
                try
                {
                    // Enlist all contexts in the connection-level transaction
                    foreach (var context in contexts)
                    {
                        context.Database.UseTransaction(transaction);
                    }

                    // Clear existing policies from all contexts
                    foreach (var context in contexts)
                    {
                        var dbSet = GetCasbinRuleDbSet(context, null);
#if NET7_0_OR_GREATER
                        // EF Core 7+: Use ExecuteDelete for better performance (set-based delete without loading entities)
                        dbSet.ExecuteDelete();
#else
                        // EF Core 3.1-6.0: Fall back to traditional approach
                        var existingRules = dbSet.ToList();
                        dbSet.RemoveRange(existingRules);
                        context.SaveChanges();
#endif
                    }

                    // Add new policies to respective contexts
                    foreach (var group in policiesByContext)
                    {
                        var context = group.Key;
                        var dbSet = GetCasbinRuleDbSet(context, null);
                        var saveRules = OnSavePolicy(store, group);
                        dbSet.AddRange(saveRules);
                        context.SaveChanges();
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            else
            {
                // Fall back to context-level transaction (for backward compatibility or when no shared connection)
                var primaryContext = contexts.First();
                using var transaction = primaryContext.Database.BeginTransaction();

                try
                {
                    // Clear existing policies from all contexts
                    foreach (var context in contexts)
                    {
                        if (context != primaryContext)
                        {
                            var dbTransaction = transaction.GetDbTransaction();
                            context.Database.UseTransaction(dbTransaction);
                        }

                        var dbSet = GetCasbinRuleDbSet(context, null);
#if NET7_0_OR_GREATER
                        // EF Core 7+: Use ExecuteDelete for better performance (set-based delete without loading entities)
                        dbSet.ExecuteDelete();
#else
                        // EF Core 3.1-6.0: Fall back to traditional approach
                        var existingRules = dbSet.ToList();
                        dbSet.RemoveRange(existingRules);
                        context.SaveChanges();
#endif
                    }

                    // Add new policies to respective contexts
                    foreach (var group in policiesByContext)
                    {
                        var context = group.Key;
                        var dbSet = GetCasbinRuleDbSet(context, null);
                        var saveRules = OnSavePolicy(store, group);
                        dbSet.AddRange(saveRules);
                        context.SaveChanges();
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        private void SavePolicyWithIndividualTransactions(IPolicyStore store, List<DbContext> contexts,
            List<IGrouping<DbContext, TPersistPolicy>> policiesByContext)
        {
            // Use separate transactions for each context (required for separate SQLite databases)
            // Note: This is not atomic across contexts but is necessary for SQLite limitations
            foreach (var context in contexts)
            {
                using var transaction = context.Database.BeginTransaction();
                try
                {
                    // Clear existing policies from this context
                    var dbSet = GetCasbinRuleDbSet(context, null);
#if NET7_0_OR_GREATER
                    // EF Core 7+: Use ExecuteDelete for better performance (set-based delete without loading entities)
                    dbSet.ExecuteDelete();
#else
                    // EF Core 3.1-6.0: Fall back to traditional approach
                    var existingRules = dbSet.ToList();
                    dbSet.RemoveRange(existingRules);
                    context.SaveChanges();
#endif

                    // Add new policies to this context
                    var policiesForContext = policiesByContext.FirstOrDefault(g => g.Key == context);
                    if (policiesForContext != null)
                    {
                        var saveRules = OnSavePolicy(store, policiesForContext);
                        dbSet.AddRange(saveRules);
                        context.SaveChanges();
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// Determines if contexts can share a transaction by checking if they use the same physical DbConnection instance.
        /// </summary>
        /// <remarks>
        /// EF Core's UseTransaction() requires that all contexts use the SAME DbConnection object instance
        /// (reference equality), not just identical connection strings. Users must explicitly create contexts
        /// with a shared DbConnection object for transaction coordination to work.
        /// </remarks>
        /// <param name="contexts">List of contexts to check for shared connection</param>
        /// <returns>True if all contexts share the same DbConnection instance; otherwise false</returns>
        private bool CanShareTransaction(List<DbContext> contexts)
        {
            // Check if all contexts share the same physical DbConnection object
            // EF Core's UseTransaction() requires reference equality, not string equality
            if (contexts.Count <= 1) return true;

            try
            {
                var firstConnection = contexts[0].Database.GetDbConnection();

                if (firstConnection == null)
                {
                    return false;
                }

                // Check reference equality - contexts must share the SAME connection object
                return contexts.All(c =>
                {
                    var connection = c.Database.GetDbConnection();
                    return ReferenceEquals(connection, firstConnection);
                });
            }
            catch (Exception)
            {
                // If we can't determine connection compatibility for any reason,
                // assume separate connections for safety
                return false;
            }
        }

        public virtual async Task SavePolicyAsync(IPolicyStore store)
        {
            var persistPolicies = new List<TPersistPolicy>();
            persistPolicies.ReadPolicyFromCasbinModel(store);

            if (persistPolicies.Count is 0)
            {
                return;
            }

            // Group policies by their target context
            var policiesByContext = persistPolicies
                .GroupBy(p => _contextProvider.GetContextForPolicyType(p.Type))
                .ToList();

            var contexts = _contextProvider.GetAllContexts().Distinct().ToList();

            Console.WriteLine($"[DIAGNOSTIC] SavePolicyAsync: {contexts.Count} contexts, {persistPolicies.Count} policies");

            // Check if we can use a shared transaction (all contexts use same connection)
            bool canShareTransaction = CanShareTransaction(contexts);
            Console.WriteLine($"[DIAGNOSTIC] CanShareTransaction returned: {canShareTransaction}");

            if (contexts.Count == 1 || canShareTransaction)
            {
                Console.WriteLine($"[DIAGNOSTIC] Using SHARED transaction path");
                // Single context or shared connection - use single transaction
                await SavePolicyWithSharedTransactionAsync(store, contexts, policiesByContext);
            }
            else
            {
                Console.WriteLine($"[DIAGNOSTIC] Using INDIVIDUAL transactions path");
                // Multiple separate databases - use individual transactions per context
                await SavePolicyWithIndividualTransactionsAsync(store, contexts, policiesByContext);
            }
        }

        private async Task SavePolicyWithSharedTransactionAsync(IPolicyStore store, List<DbContext> contexts,
            List<IGrouping<DbContext, TPersistPolicy>> policiesByContext)
        {
            var sharedConnection = _contextProvider?.GetSharedConnection();

            if (sharedConnection != null)
            {
                // Use connection-level transaction (required for PostgreSQL savepoint handling)
                Console.WriteLine($"[DIAGNOSTIC] SavePolicyWithSharedTransactionAsync: Using connection-level transaction for {contexts.Count} contexts");

                if (sharedConnection.State != System.Data.ConnectionState.Open)
                {
                    await sharedConnection.OpenAsync();
                }

                await using var transaction = await sharedConnection.BeginTransactionAsync();
                Console.WriteLine($"[DIAGNOSTIC] Connection-level transaction started: {transaction.GetType().Name}");

                try
                {
                    // Enlist all contexts in the connection-level transaction
                    Console.WriteLine($"[DIAGNOSTIC] Enlisting all {contexts.Count} contexts in connection-level transaction");
                    foreach (var context in contexts)
                    {
                        context.Database.UseTransaction(transaction);
                        Console.WriteLine($"[DIAGNOSTIC]   Enlisted: {context.GetType().Name}");
                    }

                    // Clear existing policies from all contexts
                    Console.WriteLine($"[DIAGNOSTIC] Phase 1: Deleting existing policies from {contexts.Count} contexts");
                    for (int i = 0; i < contexts.Count; i++)
                    {
                        var context = contexts[i];
                        Console.WriteLine($"[DIAGNOSTIC] Processing context {i + 1}/{contexts.Count}: {context.GetType().Name}");

                        var dbSet = GetCasbinRuleDbSet(context, null);
                        Console.WriteLine($"[DIAGNOSTIC]   Executing delete on DbSet...");
#if NET7_0_OR_GREATER
                        // EF Core 7+: Use ExecuteDeleteAsync for better performance (set-based delete without loading entities)
                        await dbSet.ExecuteDeleteAsync();
                        Console.WriteLine($"[DIAGNOSTIC]   ExecuteDeleteAsync completed");
#else
                        // EF Core 3.1-6.0: Fall back to traditional approach
                        var existingRules = await dbSet.ToListAsync();
                        dbSet.RemoveRange(existingRules);
                        await context.SaveChangesAsync();
                        Console.WriteLine($"[DIAGNOSTIC]   SaveChangesAsync completed after RemoveRange");
#endif
                    }

                    Console.WriteLine($"[DIAGNOSTIC] Phase 2: Adding new policies to {policiesByContext.Count} contexts");
                    // Add new policies to respective contexts
                    for (int i = 0; i < policiesByContext.Count; i++)
                    {
                        var group = policiesByContext[i];
                        var context = group.Key;
                        Console.WriteLine($"[DIAGNOSTIC] Adding policies {i + 1}/{policiesByContext.Count} to context: {context.GetType().Name}");

                        var dbSet = GetCasbinRuleDbSet(context, null);
                        var saveRules = OnSavePolicy(store, group);
                        Console.WriteLine($"[DIAGNOSTIC]   Adding {saveRules.Count()} policies to DbSet");
                        await dbSet.AddRangeAsync(saveRules);

                        Console.WriteLine($"[DIAGNOSTIC]   Calling SaveChangesAsync...");
                        await context.SaveChangesAsync();
                        Console.WriteLine($"[DIAGNOSTIC]   SaveChangesAsync completed");
                    }

                    Console.WriteLine($"[DIAGNOSTIC] Committing connection-level transaction...");
                    await transaction.CommitAsync();
                    Console.WriteLine($"[DIAGNOSTIC] Transaction committed successfully");

                    // Clear transaction state from all contexts to prevent SAVEPOINT errors
                    // in subsequent SaveChanges() calls
                    foreach (var context in contexts)
                    {
                        context.Database.UseTransaction(null);
                    }
                    Console.WriteLine($"[DIAGNOSTIC] Cleared transaction state from all contexts");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DIAGNOSTIC] EXCEPTION CAUGHT: {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine($"[DIAGNOSTIC] Rolling back connection-level transaction...");
                    await transaction.RollbackAsync();
                    Console.WriteLine($"[DIAGNOSTIC] Transaction rolled back");

                    // Clear transaction state from all contexts
                    foreach (var context in contexts)
                    {
                        context.Database.UseTransaction(null);
                    }
                    Console.WriteLine($"[DIAGNOSTIC] Cleared transaction state from all contexts after rollback");
                    throw;
                }
            }
            else
            {
                // Fall back to context-level transaction
                var primaryContext = contexts.First();
                Console.WriteLine($"[DIAGNOSTIC] SavePolicyWithSharedTransactionAsync: Using context-level transaction for {contexts.Count} contexts");
                Console.WriteLine($"[DIAGNOSTIC] Primary context: {primaryContext.GetType().Name}");

                await using var transaction = await primaryContext.Database.BeginTransactionAsync();
                Console.WriteLine($"[DIAGNOSTIC] Transaction started: {transaction.TransactionId}");

                try
                {
                    // Clear existing policies from all contexts
                    Console.WriteLine($"[DIAGNOSTIC] Phase 1: Deleting existing policies from {contexts.Count} contexts");
                    for (int i = 0; i < contexts.Count; i++)
                    {
                        var context = contexts[i];
                        Console.WriteLine($"[DIAGNOSTIC] Processing context {i + 1}/{contexts.Count}: {context.GetType().Name}");

                        if (context != primaryContext)
                        {
                            var dbTransaction = transaction.GetDbTransaction();
                            Console.WriteLine($"[DIAGNOSTIC]   Enlisting context in transaction via UseTransaction()");
                            // Use synchronous UseTransaction since we're just enlisting in an existing transaction
                            context.Database.UseTransaction(dbTransaction);
                        }
                        else
                        {
                            Console.WriteLine($"[DIAGNOSTIC]   This is the primary context (already in transaction)");
                        }

                        var dbSet = GetCasbinRuleDbSet(context, null);
                        Console.WriteLine($"[DIAGNOSTIC]   Executing delete on DbSet...");
#if NET7_0_OR_GREATER
                        // EF Core 7+: Use ExecuteDeleteAsync for better performance (set-based delete without loading entities)
                        await dbSet.ExecuteDeleteAsync();
                        Console.WriteLine($"[DIAGNOSTIC]   ExecuteDeleteAsync completed");
#else
                        // EF Core 3.1-6.0: Fall back to traditional approach
                        var existingRules = await dbSet.ToListAsync();
                        dbSet.RemoveRange(existingRules);
                        await context.SaveChangesAsync();
                        Console.WriteLine($"[DIAGNOSTIC]   SaveChangesAsync completed after RemoveRange");
#endif
                    }

                    Console.WriteLine($"[DIAGNOSTIC] Phase 2: Adding new policies to {policiesByContext.Count} contexts");
                    // Add new policies to respective contexts
                    for (int i = 0; i < policiesByContext.Count; i++)
                    {
                        var group = policiesByContext[i];
                        var context = group.Key;
                        Console.WriteLine($"[DIAGNOSTIC] Adding policies {i + 1}/{policiesByContext.Count} to context: {context.GetType().Name}");

                        var dbSet = GetCasbinRuleDbSet(context, null);
                        var saveRules = OnSavePolicy(store, group);
                        Console.WriteLine($"[DIAGNOSTIC]   Adding {saveRules.Count()} policies to DbSet");
                        await dbSet.AddRangeAsync(saveRules);

                        Console.WriteLine($"[DIAGNOSTIC]   Calling SaveChangesAsync...");
                        await context.SaveChangesAsync();
                        Console.WriteLine($"[DIAGNOSTIC]   SaveChangesAsync completed");
                    }

                    Console.WriteLine($"[DIAGNOSTIC] Committing transaction...");
                    await transaction.CommitAsync();
                    Console.WriteLine($"[DIAGNOSTIC] Transaction committed successfully");

                    // Clear transaction state from all contexts to prevent SAVEPOINT errors
                    foreach (var context in contexts)
                    {
                        context.Database.UseTransaction(null);
                    }
                    Console.WriteLine($"[DIAGNOSTIC] Cleared transaction state from all contexts");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DIAGNOSTIC] EXCEPTION CAUGHT: {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine($"[DIAGNOSTIC] Rolling back transaction...");
                    await transaction.RollbackAsync();
                    Console.WriteLine($"[DIAGNOSTIC] Transaction rolled back");

                    // Clear transaction state from all contexts
                    foreach (var context in contexts)
                    {
                        context.Database.UseTransaction(null);
                    }
                    Console.WriteLine($"[DIAGNOSTIC] Cleared transaction state from all contexts after rollback");
                    throw;
                }
            }
        }

        private async Task SavePolicyWithIndividualTransactionsAsync(IPolicyStore store, List<DbContext> contexts,
            List<IGrouping<DbContext, TPersistPolicy>> policiesByContext)
        {
            // Use separate transactions for each context (required for separate SQLite databases)
            // Note: This is not atomic across contexts but is necessary for SQLite limitations
            foreach (var context in contexts)
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                try
                {
                    // Clear existing policies from this context
                    var dbSet = GetCasbinRuleDbSet(context, null);
#if NET7_0_OR_GREATER
                    // EF Core 7+: Use ExecuteDeleteAsync for better performance (set-based delete without loading entities)
                    await dbSet.ExecuteDeleteAsync();
#else
                    // EF Core 3.1-6.0: Fall back to traditional approach
                    var existingRules = await dbSet.ToListAsync();
                    dbSet.RemoveRange(existingRules);
                    await context.SaveChangesAsync();
#endif

                    // Add new policies to this context
                    var policiesForContext = policiesByContext.FirstOrDefault(g => g.Key == context);
                    if (policiesForContext != null)
                    {
                        var saveRules = OnSavePolicy(store, policiesForContext);
                        await dbSet.AddRangeAsync(saveRules);
                        await context.SaveChangesAsync();
                    }

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
        }

        #endregion

        #region Add policy

        public virtual void AddPolicy(string section, string policyType, IPolicyValues values)
        {
            Console.WriteLine($"[ADAPTER] AddPolicy INVOKED: policyType={policyType}, section={section}");
            Console.WriteLine($"[ADAPTER] Call stack: {new System.Diagnostics.StackTrace(1, true)}");

            if (values.Count is 0)
            {
                Console.WriteLine($"[ADAPTER] AddPolicy: No values provided, returning");
                return;
            }

            var context = GetContextForPolicyType(policyType);
            Console.WriteLine($"[ADAPTER] Context: {context.GetType().Name}");
            var dbSet = GetCasbinRuleDbSetForPolicyType(context, policyType);
            var filter = new PolicyFilter(policyType, 0, values);
            var persistPolicies = filter.Apply(dbSet);

            if (persistPolicies.Any())
            {
                Console.WriteLine($"[ADAPTER] AddPolicy: Policy already exists, returning");
                return;
            }

            // No explicit transaction needed for individual AutoSave operations
            // EF Core will create implicit transaction for SaveChanges()
            // This prevents SAVEPOINT errors when multiple operations are called sequentially
            InternalAddPolicy(section, policyType, values);
            Console.WriteLine($"[ADAPTER] Calling context.SaveChanges() to commit immediately");
            context.SaveChanges();
            Console.WriteLine($"[ADAPTER] SaveChanges() completed");
        }

        public virtual async Task AddPolicyAsync(string section, string policyType, IPolicyValues values)
        {
            Console.WriteLine($"[ADAPTER] AddPolicyAsync INVOKED: policyType={policyType}, section={section}");
            Console.WriteLine($"[ADAPTER] Call stack: {new System.Diagnostics.StackTrace(1, true)}");

            if (values.Count is 0)
            {
                Console.WriteLine($"[ADAPTER] AddPolicyAsync: No values provided, returning");
                return;
            }

            var context = GetContextForPolicyType(policyType);
            Console.WriteLine($"[ADAPTER] Context: {context.GetType().Name}");

            var dbSet = GetCasbinRuleDbSetForPolicyType(context, policyType);
            var filter = new PolicyFilter(policyType, 0, values);
            var persistPolicies = filter.Apply(dbSet);

            if (persistPolicies.Any())
            {
                Console.WriteLine($"[ADAPTER] AddPolicyAsync: Policy already exists, returning");
                return;
            }

            // No explicit transaction needed for individual AutoSave operations
            // EF Core will create implicit transaction for SaveChangesAsync()
            // This prevents SAVEPOINT errors when multiple operations are called sequentially
            await InternalAddPolicyAsync(section, policyType, values);
            Console.WriteLine($"[ADAPTER] Calling context.SaveChangesAsync() to commit immediately");
            await context.SaveChangesAsync();
            Console.WriteLine($"[ADAPTER] SaveChangesAsync completed");
        }

        public virtual void AddPolicies(string section, string policyType,  IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0)
            {
                return;
            }

            var context = GetContextForPolicyType(policyType);

            // No explicit transaction needed for individual AutoSave operations
            InternalAddPolicies(section, policyType, valuesList);
            context.SaveChanges();
        }

        public virtual async Task AddPoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0)
            {
                return;
            }

            var context = GetContextForPolicyType(policyType);

            // No explicit transaction needed for individual AutoSave operations
            await InternalAddPoliciesAsync(section, policyType, valuesList);
            await context.SaveChangesAsync();
        }

        #endregion

        #region Remove policy

        public virtual void RemovePolicy(string section, string policyType, IPolicyValues values)
        {
            if (values.Count is 0)
            {
                return;
            }

            var context = GetContextForPolicyType(policyType);

            // No explicit transaction needed for individual AutoSave operations
            InternalRemovePolicy(section, policyType, values);
            context.SaveChanges();
        }

        public virtual async Task RemovePolicyAsync(string section, string policyType, IPolicyValues values)
        {
            if (values.Count is 0)
            {
                return;
            }

            var context = GetContextForPolicyType(policyType);

            // No explicit transaction needed for individual AutoSave operations
            InternalRemovePolicy(section, policyType, values);
            await context.SaveChangesAsync();
        }

        public virtual void RemoveFilteredPolicy(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            if (fieldValues.Count is 0)
            {
                return;
            }

            var context = GetContextForPolicyType(policyType);

            // No explicit transaction needed for individual AutoSave operations
            InternalRemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues);
            context.SaveChanges();
        }

        public virtual async Task RemoveFilteredPolicyAsync(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            if (fieldValues.Count is 0)
            {
                return;
            }

            var context = GetContextForPolicyType(policyType);

            // No explicit transaction needed for individual AutoSave operations
            InternalRemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues);
            await context.SaveChangesAsync();
        }


        public virtual void RemovePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0)
            {
                return;
            }

            var context = GetContextForPolicyType(policyType);

            // No explicit transaction needed for individual AutoSave operations
            InternalRemovePolicies(section, policyType, valuesList);
            context.SaveChanges();
        }

        public virtual async Task RemovePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0)
            {
                return;
            }

            var context = GetContextForPolicyType(policyType);

            // No explicit transaction needed for individual AutoSave operations
            InternalRemovePolicies(section, policyType, valuesList);
            await context.SaveChangesAsync();
        }

        #endregion

        #region Update policy

        public void UpdatePolicy(string section, string policyType, IPolicyValues oldValues, IPolicyValues newValues)
        {
            if (newValues.Count is 0)
            {
                return;
            }

            var context = GetContextForPolicyType(policyType);

            // No explicit transaction needed for individual AutoSave operations
            InternalUpdatePolicy(section, policyType, oldValues, newValues);
            context.SaveChanges();
        }

        public async Task UpdatePolicyAsync(string section, string policyType, IPolicyValues oldValues, IPolicyValues newValues)
        {
            if (newValues.Count is 0)
            {
                return;
            }

            var context = GetContextForPolicyType(policyType);

            // No explicit transaction needed for individual AutoSave operations
            await InternalUpdatePolicyAsync(section, policyType, oldValues, newValues);
            await context.SaveChangesAsync();
        }

        public void UpdatePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            if (newValuesList.Count is 0)
            {
                return;
            }

            var context = GetContextForPolicyType(policyType);

            // No explicit transaction needed for individual AutoSave operations
            InternalUpdatePolicies(section, policyType, oldValuesList, newValuesList);
            context.SaveChanges();
        }

        public async Task UpdatePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            if (newValuesList.Count is 0)
            {
                return;
            }

            var context = GetContextForPolicyType(policyType);

            // No explicit transaction needed for individual AutoSave operations
            await InternalUpdatePoliciesAsync(section, policyType, oldValuesList, newValuesList);
            await context.SaveChangesAsync();
        }

        #endregion

        #region IFilteredAdapter

        public bool IsFiltered { get; private set; }

        public void LoadFilteredPolicy(IPolicyStore store, IPolicyFilter filter)
        {
            var allPolicies = new List<TPersistPolicy>();

            // Load from each unique context
            foreach (var context in _contextProvider.GetAllContexts().Distinct())
            {
                var dbSet = GetCasbinRuleDbSet(context, null);
                var policies = dbSet.AsNoTracking();
                var filtered = filter.Apply(policies);
                allPolicies.AddRange(filtered.ToList());
            }

            var finalPolicies = OnLoadPolicy(store, allPolicies.AsQueryable());
            store.LoadPolicyFromPersistPolicy(finalPolicies.ToList());
            IsFiltered = true;
        }

        public async Task LoadFilteredPolicyAsync(IPolicyStore store, IPolicyFilter filter)
        {
            var allPolicies = new List<TPersistPolicy>();

            // Load from each unique context
            foreach (var context in _contextProvider.GetAllContexts().Distinct())
            {
                var dbSet = GetCasbinRuleDbSet(context, null);
                var policies = dbSet.AsNoTracking();
                var filtered = filter.Apply(policies);
                allPolicies.AddRange(await filtered.ToListAsync());
            }

            var finalPolicies = OnLoadPolicy(store, allPolicies.AsQueryable());
            store.LoadPolicyFromPersistPolicy(finalPolicies.ToList());
            IsFiltered = true;
        }

        #endregion
    }
}
