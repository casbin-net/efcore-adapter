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
            var primaryContext = contexts.First();
            using var transaction = primaryContext.Database.BeginTransaction();

            try
            {
                // Clear existing policies from all contexts
                foreach (var context in contexts)
                {
                    if (context != primaryContext)
                    {
                        var dbTransaction = (transaction as IInfrastructure<System.Data.Common.DbTransaction>)?.Instance;
                        if (dbTransaction != null)
                        {
                            context.Database.UseTransaction(dbTransaction);
                        }
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

        private bool CanShareTransaction(List<DbContext> contexts)
        {
            // Check if all contexts share the same connection string
            // For SQLite, separate database files cannot share transactions
            if (contexts.Count <= 1) return true;

            try
            {
                // Try to get connection string (available in EF Core 5.0+)
                var firstConnection = contexts[0].Database.GetDbConnection();
                var firstConnectionString = firstConnection?.ConnectionString;

                if (string.IsNullOrEmpty(firstConnectionString))
                {
                    // If we can't determine connection strings, assume separate connections
                    return false;
                }

                return contexts.All(c =>
                {
                    var connection = c.Database.GetDbConnection();
                    return connection?.ConnectionString == firstConnectionString;
                });
            }
            catch
            {
                // If we can't determine, assume separate connections for safety
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

            // Check if we can use a shared transaction (all contexts use same connection)
            if (contexts.Count == 1 || CanShareTransaction(contexts))
            {
                // Single context or shared connection - use single transaction
                await SavePolicyWithSharedTransactionAsync(store, contexts, policiesByContext);
            }
            else
            {
                // Multiple separate databases - use individual transactions per context
                await SavePolicyWithIndividualTransactionsAsync(store, contexts, policiesByContext);
            }
        }

        private async Task SavePolicyWithSharedTransactionAsync(IPolicyStore store, List<DbContext> contexts,
            List<IGrouping<DbContext, TPersistPolicy>> policiesByContext)
        {
            var primaryContext = contexts.First();
            await using var transaction = await primaryContext.Database.BeginTransactionAsync();

            try
            {
                // Clear existing policies from all contexts
                foreach (var context in contexts)
                {
                    if (context != primaryContext)
                    {
                        var dbTransaction = (transaction as IInfrastructure<System.Data.Common.DbTransaction>)?.Instance;
                        if (dbTransaction != null)
                        {
                            // Use synchronous UseTransaction since we're just enlisting in an existing transaction
                            context.Database.UseTransaction(dbTransaction);
                        }
                    }

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
                }

                // Add new policies to respective contexts
                foreach (var group in policiesByContext)
                {
                    var context = group.Key;
                    var dbSet = GetCasbinRuleDbSet(context, null);
                    var saveRules = OnSavePolicy(store, group);
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
            if (values.Count is 0)
            {
                return;
            }

            var context = GetContextForPolicyType(policyType);
            var dbSet = GetCasbinRuleDbSetForPolicyType(context, policyType);
            var filter = new PolicyFilter(policyType, 0, values);
            var persistPolicies = filter.Apply(dbSet);

            if (persistPolicies.Any())
            {
                return;
            }

            InternalAddPolicy(section, policyType, values);
            context.SaveChanges();
        }

        public virtual async Task AddPolicyAsync(string section, string policyType, IPolicyValues values)
        {
            if (values.Count is 0)
            {
                return;
            }

            var context = GetContextForPolicyType(policyType);
            var dbSet = GetCasbinRuleDbSetForPolicyType(context, policyType);
            var filter = new PolicyFilter(policyType, 0, values);
            var persistPolicies = filter.Apply(dbSet);

            if (persistPolicies.Any())
            {
                return;
            }

            await InternalAddPolicyAsync(section, policyType, values);
            await context.SaveChangesAsync();
        }

        public virtual void AddPolicies(string section, string policyType,  IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0)
            {
                return;
            }
            var context = GetContextForPolicyType(policyType);
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
            using var transaction = context.Database.BeginTransaction();
            InternalUpdatePolicy(section, policyType, oldValues, newValues);
            context.SaveChanges();
            transaction.Commit();
        }

        public async Task UpdatePolicyAsync(string section, string policyType, IPolicyValues oldValues, IPolicyValues newValues)
        {
            if (newValues.Count is 0)
            {
                return;
            }
            var context = GetContextForPolicyType(policyType);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await InternalUpdatePolicyAsync(section, policyType, oldValues, newValues);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        public void UpdatePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            if (newValuesList.Count is 0)
            {
                return;
            }
            var context = GetContextForPolicyType(policyType);
            using var transaction = context.Database.BeginTransaction();
            InternalUpdatePolicies(section, policyType, oldValuesList, newValuesList);
            context.SaveChanges();
            transaction.Commit();
        }

        public async Task UpdatePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            if (newValuesList.Count is 0)
            {
                return;
            }
            var context = GetContextForPolicyType(policyType);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await InternalUpdatePoliciesAsync(section, policyType, oldValuesList, newValuesList);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
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
