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

        public EFCoreAdapter(IServiceProvider serviceProvider) : base(serviceProvider)
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

        public EFCoreAdapter(IServiceProvider serviceProvider) : base(serviceProvider)
        {

        }
    }

    public partial class EFCoreAdapter<TKey, TPersistPolicy, TDbContext> : IAdapter, IFilteredAdapter 
        where TDbContext : DbContext
        where TPersistPolicy : class, IEFCorePersistPolicy<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        private DbSet<TPersistPolicy> _persistPolicies;
        private readonly IServiceProvider _serviceProvider;
        private readonly bool _useServiceProvider;
        
        protected TDbContext DbContext { get; private set; }
        protected DbSet<TPersistPolicy> PersistPolicies => _persistPolicies ??= GetCasbinRuleDbSet(GetOrResolveDbContext());

        public EFCoreAdapter(TDbContext context)
        {
            DbContext = context ?? throw new ArgumentNullException(nameof(context));
            _useServiceProvider = false;
        }

        public EFCoreAdapter(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _useServiceProvider = true;
        }

        private TDbContext GetOrResolveDbContext()
        {
            if (_useServiceProvider)
            {
                return _serviceProvider.GetService(typeof(TDbContext)) as TDbContext
                    ?? throw new InvalidOperationException($"Unable to resolve service for type '{typeof(TDbContext)}' from IServiceProvider.");
            }
            return DbContext;
        }

        #region Load policy

        public virtual void LoadPolicy(IPolicyStore store)
        {
            var casbinRules = PersistPolicies.AsNoTracking();
            casbinRules = OnLoadPolicy(store, casbinRules);
            store.LoadPolicyFromPersistPolicy(casbinRules.ToList());
            IsFiltered = false;
        }

        public virtual async Task LoadPolicyAsync(IPolicyStore store)
        {
            var casbinRules = PersistPolicies.AsNoTracking();
            casbinRules = OnLoadPolicy(store, casbinRules);
            store.LoadPolicyFromPersistPolicy(await casbinRules.ToListAsync());
            IsFiltered = false;
        }

        #endregion

        #region Save policy

        public virtual void SavePolicy(IPolicyStore store)
        {
            var dbContext = GetOrResolveDbContext();
            var persistPolicies = new List<TPersistPolicy>();
            persistPolicies.ReadPolicyFromCasbinModel(store);

            if (persistPolicies.Count is 0)
            {
                return;
            }

            var existRule = PersistPolicies.ToList();
            PersistPolicies.RemoveRange(existRule);
            dbContext.SaveChanges();

            var saveRules = OnSavePolicy(store, persistPolicies);
            PersistPolicies.AddRange(saveRules);
            dbContext.SaveChanges();
        }

        public virtual async Task SavePolicyAsync(IPolicyStore store)
        {
            var dbContext = GetOrResolveDbContext();
            var persistPolicies = new List<TPersistPolicy>();
            persistPolicies.ReadPolicyFromCasbinModel(store);

            if (persistPolicies.Count is 0)
            {
                return;
            }

            var existRule = PersistPolicies.ToList();
            PersistPolicies.RemoveRange(existRule);
            await dbContext.SaveChangesAsync();

            var saveRules = OnSavePolicy(store, persistPolicies);
            await PersistPolicies.AddRangeAsync(saveRules);
            await dbContext.SaveChangesAsync();
        }

        #endregion

        #region Add policy

        public virtual void AddPolicy(string section, string policyType, IPolicyValues values)
        {
            var dbContext = GetOrResolveDbContext();
            if (values.Count is 0)
            {
                return;
            }

            var filter = new PolicyFilter(policyType, 0, values);
            var persistPolicies = filter.Apply(PersistPolicies);

            if (persistPolicies.Any())
            {
                return;
            }

            InternalAddPolicy(section, policyType, values);
            dbContext.SaveChanges();
        }

        public virtual async Task AddPolicyAsync(string section, string policyType, IPolicyValues values)
        {
            var dbContext = GetOrResolveDbContext();
            if (values.Count is 0)
            {
                return;
            }

            var filter = new PolicyFilter(policyType, 0, values);
            var persistPolicies = filter.Apply(PersistPolicies);

            if (persistPolicies.Any())
            {
                return;
            }

            await InternalAddPolicyAsync(section, policyType, values);
            await dbContext.SaveChangesAsync();
        }

        public virtual void AddPolicies(string section, string policyType,  IReadOnlyList<IPolicyValues> valuesList)
        {
            var dbContext = GetOrResolveDbContext();
            if (valuesList.Count is 0)
            {
                return;
            }
            InternalAddPolicies(section, policyType, valuesList);
            dbContext.SaveChanges();
        }

        public virtual async Task AddPoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            var dbContext = GetOrResolveDbContext();
            if (valuesList.Count is 0)
            {
                return;
            }
            await InternalAddPoliciesAsync(section, policyType, valuesList);
            await dbContext.SaveChangesAsync();
        }

        #endregion

        #region Remove policy

        public virtual void RemovePolicy(string section, string policyType, IPolicyValues values)
        {
            var dbContext = GetOrResolveDbContext();
            if (values.Count is 0)
            {
                return;
            }
            InternalRemovePolicy(section, policyType, values);
            dbContext.SaveChanges();
        }

        public virtual async Task RemovePolicyAsync(string section, string policyType, IPolicyValues values)
        {
            var dbContext = GetOrResolveDbContext();
            if (values.Count is 0)
            {
                return;
            }
            InternalRemovePolicy(section, policyType, values);
            await dbContext.SaveChangesAsync();
        }

        public virtual void RemoveFilteredPolicy(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            var dbContext = GetOrResolveDbContext();
            if (fieldValues.Count is 0)
            {
                return;
            }
            InternalRemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues);
            dbContext.SaveChanges();
        }

        public virtual async Task RemoveFilteredPolicyAsync(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            var dbContext = GetOrResolveDbContext();
            if (fieldValues.Count is 0)
            {
                return;
            }
            InternalRemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues);
            await dbContext.SaveChangesAsync();
        }


        public virtual void RemovePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            var dbContext = GetOrResolveDbContext();
            if (valuesList.Count is 0)
            {
                return;
            }
            InternalRemovePolicies(section, policyType, valuesList);
            dbContext.SaveChanges();
        }

        public virtual async Task RemovePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            var dbContext = GetOrResolveDbContext();
            if (valuesList.Count is 0)
            {
                return;
            }
            InternalRemovePolicies(section, policyType, valuesList);
            await dbContext.SaveChangesAsync();
        }

        #endregion

        #region Update policy
        
        public void UpdatePolicy(string section, string policyType, IPolicyValues oldValues, IPolicyValues newValues)
        {
            var dbContext = GetOrResolveDbContext();
            if (newValues.Count is 0)
            {
                return;
            }
            using var transaction = dbContext.Database.BeginTransaction();
            InternalUpdatePolicy(section, policyType, oldValues, newValues);
            dbContext.SaveChanges();
            transaction.Commit();
        }

        public async Task UpdatePolicyAsync(string section, string policyType, IPolicyValues oldValues, IPolicyValues newValues)
        {
            var dbContext = GetOrResolveDbContext();
            if (newValues.Count is 0)
            {
                return;
            }
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            await InternalUpdatePolicyAsync(section, policyType, oldValues, newValues);
            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        public void UpdatePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            var dbContext = GetOrResolveDbContext();
            if (newValuesList.Count is 0)
            {
                return;
            }
            using var transaction = dbContext.Database.BeginTransaction();
            InternalUpdatePolicies(section, policyType, oldValuesList, newValuesList);
            dbContext.SaveChanges();
            transaction.Commit();
        }

        public async Task UpdatePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            var dbContext = GetOrResolveDbContext();
            if (newValuesList.Count is 0)
            {
                return;
            }
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            await InternalUpdatePoliciesAsync(section, policyType, oldValuesList, newValuesList);
            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        #endregion

        #region IFilteredAdapter

        public bool IsFiltered { get; private set; }
        
        public void LoadFilteredPolicy(IPolicyStore store, IPolicyFilter filter)
        {
            var persistPolicies = PersistPolicies.AsNoTracking();
            persistPolicies = filter.Apply(persistPolicies);
            persistPolicies = OnLoadPolicy(store, persistPolicies);
            store.LoadPolicyFromPersistPolicy(persistPolicies.ToList());
            IsFiltered = true;
        }

        public async Task LoadFilteredPolicyAsync(IPolicyStore store, IPolicyFilter filter)
        {
            var persistPolicies = PersistPolicies.AsNoTracking();
            persistPolicies = filter.Apply(persistPolicies);
            persistPolicies = OnLoadPolicy(store, persistPolicies);
            store.LoadPolicyFromPersistPolicy(await persistPolicies.ToListAsync());
            IsFiltered = true;
        }

        #endregion
    }
}
