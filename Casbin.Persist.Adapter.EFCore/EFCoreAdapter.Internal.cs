using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

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
            var context = GetContextForPolicyType(policyType);
            InternalAddPolicy(context, section, policyType, values);
        }

        private void InternalAddPolicy(DbContext context, string section, string policyType, IPolicyValues values)
        {
            var dbSet = GetCasbinRuleDbSetForPolicyType(context, policyType);
            var persistPolicy = PersistPolicy.Create<TPersistPolicy>(section, policyType, values);
            persistPolicy = OnAddPolicy(section, policyType, values, persistPolicy);
            dbSet.Add(persistPolicy);
        }

        private async ValueTask InternalAddPolicyAsync(string section, string policyType, IPolicyValues values)
        {
            var context = GetContextForPolicyType(policyType);
            await InternalAddPolicyAsync(context, section, policyType, values);
        }

        private async ValueTask InternalAddPolicyAsync(DbContext context, string section, string policyType, IPolicyValues values)
        {
            var dbSet = GetCasbinRuleDbSetForPolicyType(context, policyType);
            var persistPolicy = PersistPolicy.Create<TPersistPolicy>(section, policyType, values);
            persistPolicy = OnAddPolicy(section, policyType, values, persistPolicy);
            await dbSet.AddAsync(persistPolicy);
        }
        
        private void InternalUpdatePolicy(string section, string policyType, IPolicyValues oldValues , IPolicyValues newValues)
        {
            var context = GetContextForPolicyType(policyType);
            InternalUpdatePolicy(context, section, policyType, oldValues, newValues);
        }

        private void InternalUpdatePolicy(DbContext context, string section, string policyType, IPolicyValues oldValues , IPolicyValues newValues)
        {
            InternalRemovePolicy(context, section, policyType, oldValues);
            InternalAddPolicy(context, section, policyType, newValues);
        }

        private ValueTask InternalUpdatePolicyAsync(string section, string policyType, IPolicyValues oldValues , IPolicyValues newValues)
        {
            var context = GetContextForPolicyType(policyType);
            return InternalUpdatePolicyAsync(context, section, policyType, oldValues, newValues);
        }

        private async ValueTask InternalUpdatePolicyAsync(DbContext context, string section, string policyType, IPolicyValues oldValues , IPolicyValues newValues)
        {
            InternalRemovePolicy(context, section, policyType, oldValues);
            await InternalAddPolicyAsync(context, section, policyType, newValues);
        }
        
        private void InternalAddPolicies(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            var context = GetContextForPolicyType(policyType);
            InternalAddPolicies(context, section, policyType, valuesList);
        }

        private void InternalAddPolicies(DbContext context, string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            var dbSet = GetCasbinRuleDbSetForPolicyType(context, policyType);
            var persistPolicies = valuesList.
                Select(v => PersistPolicy.Create<TPersistPolicy>(section, policyType, v));
            persistPolicies = OnAddPolicies(section, policyType, valuesList, persistPolicies);
            dbSet.AddRange(persistPolicies);
        }

        private async ValueTask InternalAddPoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            var context = GetContextForPolicyType(policyType);
            await InternalAddPoliciesAsync(context, section, policyType, valuesList);
        }

        private async ValueTask InternalAddPoliciesAsync(DbContext context, string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            var dbSet = GetCasbinRuleDbSetForPolicyType(context, policyType);
            var persistPolicies = valuesList.Select(v =>
                PersistPolicy.Create<TPersistPolicy>(section, policyType, v));
            persistPolicies = OnAddPolicies(section, policyType, valuesList, persistPolicies);
            await dbSet.AddRangeAsync(persistPolicies);
        }
        
        private void InternalUpdatePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            var context = GetContextForPolicyType(policyType);
            InternalUpdatePolicies(context, section, policyType, oldValuesList, newValuesList);
        }

        private void InternalUpdatePolicies(DbContext context, string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            InternalRemovePolicies(context, section, policyType, oldValuesList);
            InternalAddPolicies(context, section, policyType, newValuesList);
        }

        private ValueTask InternalUpdatePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            var context = GetContextForPolicyType(policyType);
            return InternalUpdatePoliciesAsync(context, section, policyType, oldValuesList, newValuesList);
        }

        private async ValueTask InternalUpdatePoliciesAsync(DbContext context, string section, string policyType, IReadOnlyList<IPolicyValues> oldValuesList, IReadOnlyList<IPolicyValues> newValuesList)
        {
            InternalRemovePolicies(context, section, policyType, oldValuesList);
            await InternalAddPoliciesAsync(context, section, policyType, newValuesList);
        }
        
        private void InternalRemovePolicy(string section, string policyType, IPolicyValues values)
        {
            var context = GetContextForPolicyType(policyType);
            InternalRemovePolicy(context, section, policyType, values);
        }

        private void InternalRemovePolicy(DbContext context, string section, string policyType, IPolicyValues values)
        {
            InternalRemoveFilteredPolicy(context, section, policyType, 0, values);
        }

        private void InternalRemovePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            var context = GetContextForPolicyType(policyType);
            InternalRemovePolicies(context, section, policyType, valuesList);
        }

        private void InternalRemovePolicies(DbContext context, string section, string policyType, IReadOnlyList<IPolicyValues> valuesList)
        {
            foreach (var value in valuesList)
            {
                InternalRemovePolicy(context, section, policyType, value);
            }
        }

        private void InternalRemoveFilteredPolicy(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            var context = GetContextForPolicyType(policyType);
            InternalRemoveFilteredPolicy(context, section, policyType, fieldIndex, fieldValues);
        }

        private void InternalRemoveFilteredPolicy(DbContext context, string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            var dbSet = GetCasbinRuleDbSetForPolicyType(context, policyType);
            var filter = new PolicyFilter(policyType, fieldIndex, fieldValues);
            var persistPolicies = filter.Apply(dbSet);
            persistPolicies = OnRemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues, persistPolicies);
            dbSet.RemoveRange(persistPolicies);
        }
        
        #region Helper methods

        /// <summary>
        /// Gets or caches the DbSet for a specific context and policy type
        /// </summary>
        private DbSet<TPersistPolicy> GetCasbinRuleDbSetForPolicyType(DbContext context, string policyType)
        {
            var key = (context, policyType);
            if (!_persistPoliciesByContext.TryGetValue(key, out var dbSet))
            {
                dbSet = GetCasbinRuleDbSet(context, policyType);
                _persistPoliciesByContext[key] = dbSet;
            }
            return dbSet;
        }

        /// <summary>
        /// Gets the context responsible for handling a specific policy type
        /// </summary>
        private DbContext GetContextForPolicyType(string policyType)
        {
            return _contextProvider.GetContextForPolicyType(policyType);
        }

        #endregion

        #region virtual method

        /// <summary>
        /// Gets the DbSet for policies from the specified context (backward compatible)
        /// </summary>
        [Obsolete("Use GetCasbinRuleDbSet(DbContext, string) instead. This method will be removed in a future major version.", false)]
        protected virtual DbSet<TPersistPolicy> GetCasbinRuleDbSet(TDbContext dbContext)
        {
            return GetCasbinRuleDbSet((DbContext)dbContext, null);
        }

        /// <summary>
        /// Gets the DbSet for policies from the specified context with optional policy type routing
        /// </summary>
        protected virtual DbSet<TPersistPolicy> GetCasbinRuleDbSet(DbContext dbContext, string policyType)
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
