using Casbin.Persist;
using Casbin.Model;
using System.Collections.Generic;
using System.Linq;
using System;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Casbin.Adapter.EFCore.Extensions;
using Casbin.Adapter.EFCore.Entities;
// ReSharper disable InconsistentNaming
// ReSharper disable MemberCanBeProtected.Global
// ReSharper disable MemberCanBePrivate.Global

namespace Casbin.Adapter.EFCore
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
        private DbSet<TPersistPolicy> _casbinRules;
        protected TDbContext DbContext { get; }
        protected DbSet<TPersistPolicy> CasbinRules => _casbinRules ??= GetCasbinRuleDbSet(DbContext);

        public EFCoreAdapter(TDbContext context)
        {
            DbContext = context ?? throw new ArgumentNullException(nameof(context));
        }

        #region Load policy

        public virtual void LoadPolicy(IPolicyStore store)
        {
            var casbinRules = CasbinRules.AsNoTracking();
            casbinRules = OnLoadPolicy(store, casbinRules);
            store.LoadPolicyFromPersistPolicy(casbinRules.ToList());
            IsFiltered = false;
        }

        public virtual async Task LoadPolicyAsync(IPolicyStore store)
        {
            var casbinRules = CasbinRules.AsNoTracking();
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

            var saveRules = OnSavePolicy(store, persistPolicies);
            CasbinRules.AddRange(saveRules);
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

            var saveRules = OnSavePolicy(store, persistPolicies);
            await CasbinRules.AddRangeAsync(saveRules);
            await DbContext.SaveChangesAsync();
        }

        #endregion

        #region Add policy

        public virtual void AddPolicy(string section, string policyType, IPolicyValues values)
        {
            if (values is null || values.Count is 0)
            {
                return;
            }

            var persistPolicy = PersistPolicy.Create<TPersistPolicy>(section, policyType, values);
            persistPolicy = OnAddPolicy(section, policyType, values, persistPolicy);
            CasbinRules.Add(persistPolicy);
            DbContext.SaveChanges();
        }

        public virtual async Task AddPolicyAsync(string section, string policyType, IPolicyValues values)
        {
            var persistPolicy = PersistPolicy.Create<TPersistPolicy>(section, policyType, values);
            persistPolicy = OnAddPolicy(section, policyType, values, persistPolicy);
            await CasbinRules.AddAsync(persistPolicy);
            await DbContext.SaveChangesAsync();
        }

        public virtual void AddPolicies(string section, string policyType,  IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0)
            {
                return;
            }
            var persistPolicies = valuesList.
                Select(v => PersistPolicy.Create<TPersistPolicy>(section, policyType, v));
            persistPolicies = OnAddPolicies(section, policyType, valuesList, persistPolicies);
            CasbinRules.AddRange(persistPolicies);
            DbContext.SaveChanges();
        }

        public virtual async Task AddPoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0)
            {
                return;
            }
            var persistPolicies = valuesList.
                Select(v => PersistPolicy.Create<TPersistPolicy>(section, policyType, v));
            persistPolicies = OnAddPolicies(section, policyType, valuesList, persistPolicies);
            await CasbinRules.AddRangeAsync(persistPolicies);
            await DbContext.SaveChangesAsync();
        }

        #endregion

        #region Remove policy

        public virtual void RemovePolicy(string section, string policyType, IPolicyValues values)
        {
            InternalRemovePolicy(section, policyType, values);
            DbContext.SaveChanges();
        }

        public virtual async Task RemovePolicyAsync(string section, string policyType, IPolicyValues values)
        {
            InternalRemovePolicy(section, policyType, values);
            await DbContext.SaveChangesAsync();
        }

        public virtual void RemoveFilteredPolicy(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            if (fieldValues is null || fieldValues.Count is 0)
            {
                return;
            }
            InternalRemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues);
            DbContext.SaveChanges();
        }

        public virtual async Task RemoveFilteredPolicyAsync(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            if (fieldValues is null || fieldValues.Count is 0)
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
            foreach (var value in valuesList)
            {
                InternalRemovePolicy(section, policyType, value);
            }
            DbContext.SaveChanges();
        }

        public virtual async Task RemovePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            if (valuesList.Count is 0)
            {
                return;
            }
            foreach (var value in valuesList)
            {
                InternalRemovePolicy(section, policyType, value);
            }
            await DbContext.SaveChangesAsync();
        }

        #endregion

        #region Update policy
        
        public void UpdatePolicy(string section, string policyType, IPolicyValues oldRule, IPolicyValues newRule)
        {
            throw new NotImplementedException();
        }

        public Task UpdatePolicyAsync(string section, string policyType, IPolicyValues oldRules, IPolicyValues newRules)
        {
            throw new NotImplementedException();
        }

        public void UpdatePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> oldRules, IReadOnlyList<IPolicyValues> newRules)
        {
            throw new NotImplementedException();
        }

        public Task UpdatePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> oldRules, IReadOnlyList<IPolicyValues> newRules)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region IFilteredAdapter

        public bool IsFiltered { get; private set; }
        
        public void LoadFilteredPolicy(IPolicyStore store, IPolicyFilter filter)
        {
            var persistPolicies = CasbinRules.AsNoTracking();
            persistPolicies = filter.Apply(persistPolicies);
            persistPolicies = OnLoadPolicy(store, persistPolicies);
            store.LoadPolicyFromPersistPolicy(persistPolicies.ToList());
            IsFiltered = true;
        }

        public async Task LoadFilteredPolicyAsync(IPolicyStore store, IPolicyFilter filter)
        {
            var persistPolicies = CasbinRules.AsNoTracking();
            persistPolicies = filter.Apply(persistPolicies);
            persistPolicies = OnLoadPolicy(store, persistPolicies);
            store.LoadPolicyFromPersistPolicy(await persistPolicies.ToListAsync());
            IsFiltered = true;
        }

        #endregion
    }
}
