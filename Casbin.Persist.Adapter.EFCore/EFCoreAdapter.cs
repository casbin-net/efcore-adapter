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
    }

    public partial class EFCoreAdapter<TKey, TPersistPolicy, TDbContext> : IAdapter, IFilteredAdapter 
        where TDbContext : DbContext
        where TPersistPolicy : class, IEFCorePersistPolicy<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        private DbSet<TPersistPolicy> _persistPolicies;
        protected TDbContext DbContext { get; }
        protected DbSet<TPersistPolicy> PersistPolicies => _persistPolicies ??= GetCasbinRuleDbSet(DbContext);

        public EFCoreAdapter(TDbContext context)
        {
            DbContext = context ?? throw new ArgumentNullException(nameof(context));
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
            var persistPolicies = new List<TPersistPolicy>();
            persistPolicies.ReadPolicyFromCasbinModel(store);

            if (persistPolicies.Count is 0)
            {
                return;
            }

            var existRule = PersistPolicies.ToList();
            PersistPolicies.RemoveRange(existRule);
            DbContext.SaveChanges();

            var saveRules = OnSavePolicy(store, persistPolicies);
            PersistPolicies.AddRange(saveRules);
            DbContext.SaveChanges();
        }

        public virtual async Task SavePolicyAsync(IPolicyStore store)
        {
            var persistPolicies = new List<TPersistPolicy>();
            persistPolicies.ReadPolicyFromCasbinModel(store);

            if (persistPolicies.Count is 0)
            {
                return;
            }

            var existRule = PersistPolicies.ToList();
            PersistPolicies.RemoveRange(existRule);
            await DbContext.SaveChangesAsync();

            var saveRules = OnSavePolicy(store, persistPolicies);
            await PersistPolicies.AddRangeAsync(saveRules);
            await DbContext.SaveChangesAsync();
        }

        #endregion

        #region Add policy

        public virtual void AddPolicy(string section, string policyType, IPolicyValues values)
        {
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
            DbContext.SaveChanges();
        }

        public virtual async Task AddPolicyAsync(string section, string policyType, IPolicyValues values)
        {
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
            await DbContext.SaveChangesAsync();
        }

        public virtual void AddPolicies(string section, string policyType,  IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0)
            {
                return;
            }
            InternalAddPolicies(section, policyType, valuesList);
            DbContext.SaveChanges();
        }

        public virtual async Task AddPoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0)
            {
                return;
            }
            await InternalAddPoliciesAsync(section, policyType, valuesList);
            await DbContext.SaveChangesAsync();
        }

        #endregion

        #region Remove policy

        public virtual void RemovePolicy(string section, string policyType, IPolicyValues values)
        {
            if (values.Count is 0)
            {
                return;
            }
            InternalRemovePolicy(section, policyType, values);
            DbContext.SaveChanges();
        }

        public virtual async Task RemovePolicyAsync(string section, string policyType, IPolicyValues values)
        {
            if (values.Count is 0)
            {
                return;
            }
            InternalRemovePolicy(section, policyType, values);
            await DbContext.SaveChangesAsync();
        }

        public virtual void RemoveFilteredPolicy(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            if (fieldValues.Count is 0)
            {
                return;
            }
            InternalRemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues);
            DbContext.SaveChanges();
        }

        public virtual async Task RemoveFilteredPolicyAsync(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            if (fieldValues.Count is 0)
            {
                return;
            }
            InternalRemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues);
            await DbContext.SaveChangesAsync();
        }


        public virtual void RemovePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0)
            {
                return;
            }
            InternalRemovePolicies(section, policyType, valuesList);
            DbContext.SaveChanges();
        }

        public virtual async Task RemovePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0)
            {
                return;
            }
            InternalRemovePolicies(section, policyType, valuesList);
            await DbContext.SaveChangesAsync();
        }

        #endregion

        #region Update policy
        
        public void UpdatePolicy(string section, string policyType, IPolicyValues oldValues, IPolicyValues newValues)
        {
            if (newValues.Count is 0)
            {
                return;
            }
            using var transaction = DbContext.Database.BeginTransaction();
            InternalUpdatePolicy(section, policyType, oldValues, newValues);
            DbContext.SaveChanges();
            transaction.Commit();
        }

        public async Task UpdatePolicyAsync(string section, string policyType, IPolicyValues oldValues, IPolicyValues newValues)
        {
            if (newValues.Count is 0)
            {
                return;
            }
            await using var transaction = await DbContext.Database.BeginTransactionAsync();
            await InternalUpdatePolicyAsync(section, policyType, oldValues, newValues);
            await DbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        public void UpdatePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            if (newValuesList.Count is 0)
            {
                return;
            }
            using var transaction = DbContext.Database.BeginTransaction();
            InternalUpdatePolicies(section, policyType, oldValuesList, newValuesList);
            DbContext.SaveChanges();
            transaction.Commit();
        }

        public async Task UpdatePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            if (newValuesList.Count is 0)
            {
                return;
            }
            await using var transaction = await DbContext.Database.BeginTransactionAsync();
            await InternalUpdatePoliciesAsync(section, policyType, oldValuesList, newValuesList);
            await DbContext.SaveChangesAsync();
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
