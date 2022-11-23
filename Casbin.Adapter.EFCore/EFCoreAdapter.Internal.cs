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
    public partial class EFCoreAdapter<TKey, TPersistPolicy, TDbContext> : IAdapter, IFilteredAdapter 
        where TDbContext : DbContext
        where TPersistPolicy : class, IEFCorePersistPolicy<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        private void InternalRemovePolicy(string section, string policyType, IPolicyValues values)
        {
            RemoveFilteredPolicy(section, policyType, 0, values);
        }

        private void InternalRemoveFilteredPolicy(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            var filter = new PolicyFilter(policyType, fieldIndex, fieldValues);
            var persistPolicies = filter.Apply(CasbinRules);
            persistPolicies = OnRemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues, persistPolicies);
            CasbinRules.RemoveRange(persistPolicies);
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
