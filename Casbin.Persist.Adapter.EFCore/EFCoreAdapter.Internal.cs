using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin.Model;
using Microsoft.EntityFrameworkCore;

// ReSharper disable InconsistentNaming
// ReSharper disable MemberCanBeProtected.Global
// ReSharper disable MemberCanBePrivate.Global

namespace Casbin.Persist.Adapter.EFCore
{
    public partial class EFCoreAdapter<TKey, TPersistPolicy, TDbContext> : IAdapter, IFilteredAdapter 
        where TDbContext : DbContext
        where TPersistPolicy : class, IEFCorePersistPolicy<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        private void InternalAddPolicy(string section, string policyType, IPolicyValues values)
        {
            var persistPolicy = PersistPolicy.Create<TPersistPolicy>(section, policyType, values);
            persistPolicy = OnAddPolicy(section, policyType, values, persistPolicy);
            PersistPolicies.Add(persistPolicy);
        }

        private async ValueTask InternalAddPolicyAsync(string section, string policyType, IPolicyValues values)
        {
            var persistPolicy = PersistPolicy.Create<TPersistPolicy>(section, policyType, values);
            persistPolicy = OnAddPolicy(section, policyType, values, persistPolicy);
            await PersistPolicies.AddAsync(persistPolicy);
        }
        
        private void InternalUpdatePolicy(string section, string policyType, IPolicyValues oldValues , IPolicyValues newValues)
        {
            InternalRemovePolicy(section, policyType, oldValues);
            InternalAddPolicy(section, policyType, newValues);
        }

        private ValueTask InternalUpdatePolicyAsync(string section, string policyType, IPolicyValues oldValues , IPolicyValues newValues)
        {
            InternalRemovePolicy(section, policyType, oldValues);
            return InternalAddPolicyAsync(section, policyType, newValues);
        }
        
        private void InternalAddPolicies(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            var persistPolicies = valuesList.
                Select(v => PersistPolicy.Create<TPersistPolicy>(section, policyType, v));
            persistPolicies = OnAddPolicies(section, policyType, valuesList, persistPolicies);
            PersistPolicies.AddRange(persistPolicies);
        }

        private async ValueTask InternalAddPoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            var persistPolicies = valuesList.Select(v => 
                PersistPolicy.Create<TPersistPolicy>(section, policyType, v));
            persistPolicies = OnAddPolicies(section, policyType, valuesList, persistPolicies);
            await PersistPolicies.AddRangeAsync(persistPolicies);
        }
        
        private void InternalUpdatePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            InternalRemovePolicies(section, policyType, oldValuesList);
            InternalAddPolicies(section, policyType, newValuesList);
        }

        private ValueTask InternalUpdatePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            InternalRemovePolicies(section, policyType, oldValuesList);
            return InternalAddPoliciesAsync(section, policyType, newValuesList);
        }
        
        private void InternalRemovePolicy(string section, string policyType, IPolicyValues values)
        {
            RemoveFilteredPolicy(section, policyType, 0, values);
        }
        
        private void InternalRemovePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            foreach (var value in valuesList)
            {
                InternalRemovePolicy(section, policyType, value);
            }
        }

        private void InternalRemoveFilteredPolicy(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            var filter = new PolicyFilter(policyType, fieldIndex, fieldValues);
            var persistPolicies = filter.Apply(PersistPolicies);
            persistPolicies = OnRemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues, persistPolicies);
            PersistPolicies.RemoveRange(persistPolicies);
        }
        
        #region virtual method

        protected virtual DbSet<TPersistPolicy> GetCasbinRuleDbSet(TDbContext dbContext)
        {
            return dbContext.Set<TPersistPolicy>();
        }

        protected virtual IQueryable<TPersistPolicy> OnLoadPolicy(IPolicyStore store, IQueryable<TPersistPolicy> policies)
        {
            return policies;
        }

        protected virtual IEnumerable<TPersistPolicy> OnSavePolicy(IPolicyStore store, IEnumerable<TPersistPolicy> policies)
        {
            return policies;
        }

        protected virtual TPersistPolicy OnAddPolicy(string section, string policyType, IPolicyValues values, TPersistPolicy policy)
        {
            return policy;
        }

        protected virtual IEnumerable<TPersistPolicy> OnAddPolicies(string section, string policyType,
            IEnumerable<IEnumerable<string>> addList, IEnumerable<TPersistPolicy> policies)
        {
            return policies;
        }

        protected virtual IQueryable<TPersistPolicy> OnRemoveFilteredPolicy(string section, string policyType, int fieldIndex,
            IPolicyValues fieldValues, IQueryable<TPersistPolicy> policies)
        {
            return policies;
        }
        
        #endregion
    }
}
