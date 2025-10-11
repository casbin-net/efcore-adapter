using Casbin.Model;
using System.Collections.Generic;
using System.Linq;
using System;
using Microsoft.EntityFrameworkCore;
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

#if NET5_0_OR_GREATER
        public EFCoreAdapter(IDbContextFactory<CasbinDbContext<TKey>> contextFactory) : base(contextFactory)
        {

        }
#endif
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

#if NET5_0_OR_GREATER
        // For dependency injection with DbContext factory (recommended for DI scenarios)
        // ReSharper disable once SuggestBaseTypeForParameter
        public EFCoreAdapter(IDbContextFactory<CasbinDbContext<TKey>> contextFactory) : base(contextFactory)
        {

        }
#endif
    }

    public partial class EFCoreAdapter<TKey, TPersistPolicy, TDbContext> : IAdapter, IFilteredAdapter 
        where TDbContext : DbContext
        where TPersistPolicy : class, IEFCorePersistPolicy<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        private DbSet<TPersistPolicy> _persistPolicies;
        private readonly TDbContext _context;
#if NET5_0_OR_GREATER
        private readonly IDbContextFactory<TDbContext> _contextFactory;
        private readonly bool _useFactory;
#endif

        protected TDbContext DbContext
        {
            get
            {
#if NET5_0_OR_GREATER
                if (_useFactory)
                {
                    throw new InvalidOperationException(
                        "Cannot access DbContext directly when using IDbContextFactory. " +
                        "The adapter creates and disposes contexts automatically for each operation.");
                }
#endif
                return _context;
            }
        }

        protected DbSet<TPersistPolicy> PersistPolicies => _persistPolicies ??= GetCasbinRuleDbSet(_context);

        public EFCoreAdapter(TDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
#if NET5_0_OR_GREATER
            _useFactory = false;
#endif
        }

#if NET5_0_OR_GREATER
        public EFCoreAdapter(IDbContextFactory<TDbContext> contextFactory)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _useFactory = true;
        }

        private TDbContext CreateDbContext()
        {
            return _contextFactory.CreateDbContext();
        }

        private async ValueTask<TDbContext> CreateDbContextAsync()
        {
#if NET6_0_OR_GREATER
            return await _contextFactory.CreateDbContextAsync();
#else
            // EF Core 5.0 doesn't have CreateDbContextAsync, use synchronous version
            return await Task.FromResult(_contextFactory.CreateDbContext());
#endif
        }
#endif

        #region Load policy

        public virtual void LoadPolicy(IPolicyStore store)
        {
#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                using var context = CreateDbContext();
                var casbinRules = GetCasbinRuleDbSet(context).AsNoTracking();
                casbinRules = OnLoadPolicy(store, casbinRules);
                store.LoadPolicyFromPersistPolicy(casbinRules.ToList());
                IsFiltered = false;
                return;
            }
#endif
            var rules = PersistPolicies.AsNoTracking();
            rules = OnLoadPolicy(store, rules);
            store.LoadPolicyFromPersistPolicy(rules.ToList());
            IsFiltered = false;
        }

        public virtual async Task LoadPolicyAsync(IPolicyStore store)
        {
#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                await using var context = await CreateDbContextAsync();
                var casbinRules = GetCasbinRuleDbSet(context).AsNoTracking();
                casbinRules = OnLoadPolicy(store, casbinRules);
                store.LoadPolicyFromPersistPolicy(await casbinRules.ToListAsync());
                IsFiltered = false;
                return;
            }
#endif
            var rules = PersistPolicies.AsNoTracking();
            rules = OnLoadPolicy(store, rules);
            store.LoadPolicyFromPersistPolicy(await rules.ToListAsync());
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

#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                using var context = CreateDbContext();
                var policies = GetCasbinRuleDbSet(context);
                var existRule = policies.ToList();
                policies.RemoveRange(existRule);
                context.SaveChanges();

                var saveRules = OnSavePolicy(store, persistPolicies);
                policies.AddRange(saveRules);
                context.SaveChanges();
                return;
            }
#endif

            var existingRules = PersistPolicies.ToList();
            PersistPolicies.RemoveRange(existingRules);
            _context.SaveChanges();

            var rulesToSave = OnSavePolicy(store, persistPolicies);
            PersistPolicies.AddRange(rulesToSave);
            _context.SaveChanges();
        }

        public virtual async Task SavePolicyAsync(IPolicyStore store)
        {
            var persistPolicies = new List<TPersistPolicy>();
            persistPolicies.ReadPolicyFromCasbinModel(store);

            if (persistPolicies.Count is 0)
            {
                return;
            }

#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                await using var context = await CreateDbContextAsync();
                var policies = GetCasbinRuleDbSet(context);
                var existRule = policies.ToList();
                policies.RemoveRange(existRule);
                await context.SaveChangesAsync();

                var saveRules = OnSavePolicy(store, persistPolicies);
                await policies.AddRangeAsync(saveRules);
                await context.SaveChangesAsync();
                return;
            }
#endif

            var existingRules = PersistPolicies.ToList();
            PersistPolicies.RemoveRange(existingRules);
            await _context.SaveChangesAsync();

            var rulesToSave = OnSavePolicy(store, persistPolicies);
            await PersistPolicies.AddRangeAsync(rulesToSave);
            await _context.SaveChangesAsync();
        }

        #endregion

        #region Add policy

        public virtual void AddPolicy(string section, string policyType, IPolicyValues values)
        {
            if (values.Count is 0)
            {
                return;
            }

#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                using var context = CreateDbContext();
                var policies = GetCasbinRuleDbSet(context);
                var filter = new PolicyFilter(policyType, 0, values);
                var existingPolicies = filter.Apply(policies);
                if (existingPolicies.Any())
                {
                    return;
                }
                var persistPolicy = PersistPolicy.Create<TPersistPolicy>(section, policyType, values);
                persistPolicy = OnAddPolicy(section, policyType, values, persistPolicy);
                policies.Add(persistPolicy);
                context.SaveChanges();
                return;
            }
#endif

            var policyFilter = new PolicyFilter(policyType, 0, values);
            var persistPolicies = policyFilter.Apply(PersistPolicies);

            if (persistPolicies.Any())
            {
                return;
            }

            InternalAddPolicy(section, policyType, values);
            _context.SaveChanges();
        }

        public virtual async Task AddPolicyAsync(string section, string policyType, IPolicyValues values)
        {
            if (values.Count is 0)
            {
                return;
            }

#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                await using var context = await CreateDbContextAsync();
                var policies = GetCasbinRuleDbSet(context);
                var filter = new PolicyFilter(policyType, 0, values);
                var existingPolicies = filter.Apply(policies);
                if (existingPolicies.Any())
                {
                    return;
                }
                var persistPolicy = PersistPolicy.Create<TPersistPolicy>(section, policyType, values);
                persistPolicy = OnAddPolicy(section, policyType, values, persistPolicy);
                await policies.AddAsync(persistPolicy);
                await context.SaveChangesAsync();
                return;
            }
#endif

            var policyFilter = new PolicyFilter(policyType, 0, values);
            var persistPolicies = policyFilter.Apply(PersistPolicies);

            if (persistPolicies.Any())
            {
                return;
            }

            await InternalAddPolicyAsync(section, policyType, values);
            await _context.SaveChangesAsync();
        }

        public virtual void AddPolicies(string section, string policyType,  IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0)
            {
                return;
            }

#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                using var context = CreateDbContext();
                var policies = GetCasbinRuleDbSet(context);
                var persistPolicies = valuesList.Select(v => PersistPolicy.Create<TPersistPolicy>(section, policyType, v));
                persistPolicies = OnAddPolicies(section, policyType, valuesList, persistPolicies);
                policies.AddRange(persistPolicies);
                context.SaveChanges();
                return;
            }
#endif

            InternalAddPolicies(section, policyType, valuesList);
            _context.SaveChanges();
        }

        public virtual async Task AddPoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0)
            {
                return;
            }

#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                await using var context = await CreateDbContextAsync();
                var policies = GetCasbinRuleDbSet(context);
                var persistPolicies = valuesList.Select(v => PersistPolicy.Create<TPersistPolicy>(section, policyType, v));
                persistPolicies = OnAddPolicies(section, policyType, valuesList, persistPolicies);
                await policies.AddRangeAsync(persistPolicies);
                await context.SaveChangesAsync();
                return;
            }
#endif

            await InternalAddPoliciesAsync(section, policyType, valuesList);
            await _context.SaveChangesAsync();
        }

        #endregion

        #region Remove policy

        public virtual void RemovePolicy(string section, string policyType, IPolicyValues values)
        {
            if (values.Count is 0)
            {
                return;
            }

#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                using var context = CreateDbContext();
                var policies = GetCasbinRuleDbSet(context);
                var filter = new PolicyFilter(policyType, 0, values);
                var persistPolicies = filter.Apply(policies);
                persistPolicies = OnRemoveFilteredPolicy(section, policyType, 0, values, persistPolicies);
                policies.RemoveRange(persistPolicies);
                context.SaveChanges();
                return;
            }
#endif

            InternalRemovePolicy(section, policyType, values);
            _context.SaveChanges();
        }

        public virtual async Task RemovePolicyAsync(string section, string policyType, IPolicyValues values)
        {
            if (values.Count is 0)
            {
                return;
            }

#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                await using var context = await CreateDbContextAsync();
                var policies = GetCasbinRuleDbSet(context);
                var filter = new PolicyFilter(policyType, 0, values);
                var persistPolicies = filter.Apply(policies);
                persistPolicies = OnRemoveFilteredPolicy(section, policyType, 0, values, persistPolicies);
                policies.RemoveRange(persistPolicies);
                await context.SaveChangesAsync();
                return;
            }
#endif

            InternalRemovePolicy(section, policyType, values);
            await _context.SaveChangesAsync();
        }

        public virtual void RemoveFilteredPolicy(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            if (fieldValues.Count is 0)
            {
                return;
            }

#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                using var context = CreateDbContext();
                var policies = GetCasbinRuleDbSet(context);
                var filter = new PolicyFilter(policyType, fieldIndex, fieldValues);
                var persistPolicies = filter.Apply(policies);
                persistPolicies = OnRemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues, persistPolicies);
                policies.RemoveRange(persistPolicies);
                context.SaveChanges();
                return;
            }
#endif

            InternalRemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues);
            _context.SaveChanges();
        }

        public virtual async Task RemoveFilteredPolicyAsync(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            if (fieldValues.Count is 0)
            {
                return;
            }

#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                await using var context = await CreateDbContextAsync();
                var policies = GetCasbinRuleDbSet(context);
                var filter = new PolicyFilter(policyType, fieldIndex, fieldValues);
                var persistPolicies = filter.Apply(policies);
                persistPolicies = OnRemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues, persistPolicies);
                policies.RemoveRange(persistPolicies);
                await context.SaveChangesAsync();
                return;
            }
#endif

            InternalRemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues);
            await _context.SaveChangesAsync();
        }


        public virtual void RemovePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0)
            {
                return;
            }

#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                using var context = CreateDbContext();
                var policies = GetCasbinRuleDbSet(context);
                foreach (var value in valuesList)
                {
                    var filter = new PolicyFilter(policyType, 0, value);
                    var persistPolicies = filter.Apply(policies);
                    persistPolicies = OnRemoveFilteredPolicy(section, policyType, 0, value, persistPolicies);
                    policies.RemoveRange(persistPolicies);
                }
                context.SaveChanges();
                return;
            }
#endif

            InternalRemovePolicies(section, policyType, valuesList);
            _context.SaveChanges();
        }

        public virtual async Task RemovePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0)
            {
                return;
            }

#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                await using var context = await CreateDbContextAsync();
                var policies = GetCasbinRuleDbSet(context);
                foreach (var value in valuesList)
                {
                    var filter = new PolicyFilter(policyType, 0, value);
                    var persistPolicies = filter.Apply(policies);
                    persistPolicies = OnRemoveFilteredPolicy(section, policyType, 0, value, persistPolicies);
                    policies.RemoveRange(persistPolicies);
                }
                await context.SaveChangesAsync();
                return;
            }
#endif

            InternalRemovePolicies(section, policyType, valuesList);
            await _context.SaveChangesAsync();
        }

        #endregion

        #region Update policy
        
        public void UpdatePolicy(string section, string policyType, IPolicyValues oldValues, IPolicyValues newValues)
        {
            if (newValues.Count is 0)
            {
                return;
            }

#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                using var context = CreateDbContext();
                using var transaction = context.Database.BeginTransaction();
                var policies = GetCasbinRuleDbSet(context);
                
                // Remove old policy
                var filterOld = new PolicyFilter(policyType, 0, oldValues);
                var oldPolicies = filterOld.Apply(policies);
                oldPolicies = OnRemoveFilteredPolicy(section, policyType, 0, oldValues, oldPolicies);
                policies.RemoveRange(oldPolicies);
                
                // Add new policy
                var persistPolicy = PersistPolicy.Create<TPersistPolicy>(section, policyType, newValues);
                persistPolicy = OnAddPolicy(section, policyType, newValues, persistPolicy);
                policies.Add(persistPolicy);
                
                context.SaveChanges();
                transaction.Commit();
                return;
            }
#endif

            using var trans = _context.Database.BeginTransaction();
            InternalUpdatePolicy(section, policyType, oldValues, newValues);
            _context.SaveChanges();
            trans.Commit();
        }

        public async Task UpdatePolicyAsync(string section, string policyType, IPolicyValues oldValues, IPolicyValues newValues)
        {
            if (newValues.Count is 0)
            {
                return;
            }

#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                await using var context = await CreateDbContextAsync();
                await using var transaction = await context.Database.BeginTransactionAsync();
                var policies = GetCasbinRuleDbSet(context);
                
                // Remove old policy
                var filterOld = new PolicyFilter(policyType, 0, oldValues);
                var oldPolicies = filterOld.Apply(policies);
                oldPolicies = OnRemoveFilteredPolicy(section, policyType, 0, oldValues, oldPolicies);
                policies.RemoveRange(oldPolicies);
                
                // Add new policy
                var persistPolicy = PersistPolicy.Create<TPersistPolicy>(section, policyType, newValues);
                persistPolicy = OnAddPolicy(section, policyType, newValues, persistPolicy);
                await policies.AddAsync(persistPolicy);
                
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
                return;
            }
#endif

            await using var trans = await _context.Database.BeginTransactionAsync();
            await InternalUpdatePolicyAsync(section, policyType, oldValues, newValues);
            await _context.SaveChangesAsync();
            await trans.CommitAsync();
        }

        public void UpdatePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            if (newValuesList.Count is 0)
            {
                return;
            }

#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                using var context = CreateDbContext();
                using var transaction = context.Database.BeginTransaction();
                var policies = GetCasbinRuleDbSet(context);
                
                // Remove old policies
                foreach (var oldValue in oldValuesList)
                {
                    var filterOld = new PolicyFilter(policyType, 0, oldValue);
                    var oldPolicies = filterOld.Apply(policies);
                    oldPolicies = OnRemoveFilteredPolicy(section, policyType, 0, oldValue, oldPolicies);
                    policies.RemoveRange(oldPolicies);
                }
                
                // Add new policies
                var persistPolicies = newValuesList.Select(v => PersistPolicy.Create<TPersistPolicy>(section, policyType, v));
                persistPolicies = OnAddPolicies(section, policyType, newValuesList, persistPolicies);
                policies.AddRange(persistPolicies);
                
                context.SaveChanges();
                transaction.Commit();
                return;
            }
#endif

            using var trans = _context.Database.BeginTransaction();
            InternalUpdatePolicies(section, policyType, oldValuesList, newValuesList);
            _context.SaveChanges();
            trans.Commit();
        }

        public async Task UpdatePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            if (newValuesList.Count is 0)
            {
                return;
            }

#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                await using var context = await CreateDbContextAsync();
                await using var transaction = await context.Database.BeginTransactionAsync();
                var policies = GetCasbinRuleDbSet(context);
                
                // Remove old policies
                foreach (var oldValue in oldValuesList)
                {
                    var filterOld = new PolicyFilter(policyType, 0, oldValue);
                    var oldPolicies = filterOld.Apply(policies);
                    oldPolicies = OnRemoveFilteredPolicy(section, policyType, 0, oldValue, oldPolicies);
                    policies.RemoveRange(oldPolicies);
                }
                
                // Add new policies
                var persistPolicies = newValuesList.Select(v => PersistPolicy.Create<TPersistPolicy>(section, policyType, v));
                persistPolicies = OnAddPolicies(section, policyType, newValuesList, persistPolicies);
                await policies.AddRangeAsync(persistPolicies);
                
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
                return;
            }
#endif

            await using var trans = await _context.Database.BeginTransactionAsync();
            await InternalUpdatePoliciesAsync(section, policyType, oldValuesList, newValuesList);
            await _context.SaveChangesAsync();
            await trans.CommitAsync();
        }

        #endregion

        #region IFilteredAdapter

        public bool IsFiltered { get; private set; }
        
        public void LoadFilteredPolicy(IPolicyStore store, IPolicyFilter filter)
        {
#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                using var context = CreateDbContext();
                var policies = GetCasbinRuleDbSet(context).AsNoTracking();
                policies = filter.Apply(policies);
                policies = OnLoadPolicy(store, policies);
                store.LoadPolicyFromPersistPolicy(policies.ToList());
                IsFiltered = true;
                return;
            }
#endif

            var persistPolicies = PersistPolicies.AsNoTracking();
            persistPolicies = filter.Apply(persistPolicies);
            persistPolicies = OnLoadPolicy(store, persistPolicies);
            store.LoadPolicyFromPersistPolicy(persistPolicies.ToList());
            IsFiltered = true;
        }

        public async Task LoadFilteredPolicyAsync(IPolicyStore store, IPolicyFilter filter)
        {
#if NET5_0_OR_GREATER
            if (_useFactory)
            {
                await using var context = await CreateDbContextAsync();
                var policies = GetCasbinRuleDbSet(context).AsNoTracking();
                policies = filter.Apply(policies);
                policies = OnLoadPolicy(store, policies);
                store.LoadPolicyFromPersistPolicy(await policies.ToListAsync());
                IsFiltered = true;
                return;
            }
#endif

            var persistPolicies = PersistPolicies.AsNoTracking();
            persistPolicies = filter.Apply(persistPolicies);
            persistPolicies = OnLoadPolicy(store, persistPolicies);
            store.LoadPolicyFromPersistPolicy(await persistPolicies.ToListAsync());
            IsFiltered = true;
        }

        #endregion
    }
}
