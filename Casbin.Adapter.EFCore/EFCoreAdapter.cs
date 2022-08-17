using Casbin.Model;
using Casbin.Persist;
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
    public class EFCoreAdapter<TKey> : EFCoreAdapter<TKey, CasbinRule<TKey>> where TKey : IEquatable<TKey>
    {
        public EFCoreAdapter(CasbinDbContext<TKey> context) : base(context)
        {

        }
    }

    public class EFCoreAdapter<TKey, TCasbinRule> : EFCoreAdapter<TKey, TCasbinRule, CasbinDbContext<TKey>>
        where TCasbinRule : class, ICasbinRule<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        // For dependency injection to use.
        // ReSharper disable once SuggestBaseTypeForParameter
        public EFCoreAdapter(CasbinDbContext<TKey> context) : base(context)
        {

        }
    }

    public class EFCoreAdapter<TKey, TCasbinRule, TDbContext> : IAdapter, IFilteredAdapter 
        where TDbContext : DbContext
        where TCasbinRule : class, ICasbinRule<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        protected TDbContext DbContext { get; }
        protected DbSet<TCasbinRule> CasbinRules => _casbinRules ??= GetCasbinRuleDbSet(DbContext);
        private DbSet<TCasbinRule> _casbinRules;

        public EFCoreAdapter(TDbContext context)
        {
            DbContext = context ?? throw new ArgumentNullException(nameof(context));
        }

        #region virtual method

        protected virtual DbSet<TCasbinRule> GetCasbinRuleDbSet(TDbContext dbContext)
        {
            return dbContext.Set<TCasbinRule>();
        }

        protected virtual IQueryable<TCasbinRule> OnLoadPolicy(IPolicyStore model, IQueryable<TCasbinRule> casbinRules)
        {
            return casbinRules;
        }

        protected virtual IEnumerable<TCasbinRule> OnSavePolicy(IPolicyStore model, IEnumerable<TCasbinRule> casbinRules)
        {
            return casbinRules;
        }

        protected virtual TCasbinRule OnAddPolicy(string section, string policyType, IPolicyValues rule, TCasbinRule casbinRule)
        {
            return casbinRule;
        }

        protected virtual IEnumerable<TCasbinRule> OnAddPolicies(string section, string policyType,
            IEnumerable<IEnumerable<string>> rules, IEnumerable<TCasbinRule> casbinRules)
        {
            return casbinRules;
        }

        protected virtual IQueryable<TCasbinRule> OnRemoveFilteredPolicy(string section, string policyType, int fieldIndex, IPolicyValues fieldValues, IQueryable<TCasbinRule> casbinRules)
        {
            return casbinRules;
        }

        #endregion

        #region Load policy

        public virtual void LoadPolicy(IPolicyStore model)
        {
            var casbinRules = CasbinRules.AsNoTracking();
            casbinRules = OnLoadPolicy(model, casbinRules);
            model.LoadPolicyFromCasbinRules(casbinRules.ToList());
            IsFiltered = false;
        }

        public virtual async Task LoadPolicyAsync(IPolicyStore model)
        {
            var casbinRules = CasbinRules.AsNoTracking();
            casbinRules = OnLoadPolicy(model, casbinRules);
            model.LoadPolicyFromCasbinRules(await casbinRules.ToListAsync());
            IsFiltered = false;
        }

        #endregion

        #region Save policy

        public virtual void SavePolicy(IPolicyStore model)
        {
            var casbinRules = new List<TCasbinRule>();
            casbinRules.ReadPolicyFromCasbinModel(model);

            if (casbinRules.Count is 0)
            {
                return;
            }

            var saveRules = OnSavePolicy(model, casbinRules);
            CasbinRules.AddRange(saveRules);
            DbContext.SaveChanges();
        }

        public virtual async Task SavePolicyAsync(IPolicyStore model)
        {
            var casbinRules = new List<TCasbinRule>();
            casbinRules.ReadPolicyFromCasbinModel(model);

            if (casbinRules.Count is 0)
            {
                return;
            }

            var saveRules = OnSavePolicy(model, casbinRules);
            await CasbinRules.AddRangeAsync(saveRules);
            await DbContext.SaveChangesAsync();
        }

        #endregion

        #region Add policy

        public virtual void AddPolicy(string section, string policyType, IPolicyValues rule)
        {
            if (rule is null || rule.Count is 0)
            {
                return;
            }

            var casbinRule = CasbinRuleExtenstion.Parse<TCasbinRule>(policyType, rule);
            casbinRule = OnAddPolicy(section, policyType, rule, casbinRule);
            CasbinRules.Add(casbinRule);
            DbContext.SaveChanges();
        }

        public virtual async Task AddPolicyAsync(string section, string policyType, IPolicyValues rule)
        {
            if (rule is null || rule.Count is 0)
            {
                return;
            }

            var casbinRule = CasbinRuleExtenstion.Parse<TCasbinRule>(policyType, rule);
            casbinRule = OnAddPolicy(section, policyType, rule, casbinRule);
            await CasbinRules.AddAsync(casbinRule);
            await DbContext.SaveChangesAsync();
        }

        public virtual void AddPolicies(string section, string policyType, IReadOnlyList<IPolicyValues> rules)
        {
            if (rules is null)
            {
                return;
            }

            var rulesArray = rules as IPolicyValues[] ?? rules.ToArray();
            if (rulesArray.Length is 0)
            {
                return;
            }

            var casbinRules = rulesArray.Select(r => 
                CasbinRuleExtenstion.Parse<TCasbinRule>(policyType, r));
            casbinRules = OnAddPolicies(section, policyType, rulesArray, casbinRules);
            CasbinRules.AddRange(casbinRules);
            DbContext.SaveChanges();
        }

        public virtual async Task AddPoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> rules)
        {
            if (rules is null)
            {
                return;
            }

            var rulesArray = rules as IPolicyValues[] ?? rules.ToArray();
            if (rulesArray.Length is 0)
            {
                return;
            }

            var casbinRules = rulesArray.Select(r => 
                CasbinRuleExtenstion.Parse<TCasbinRule>(policyType, r));
            casbinRules = OnAddPolicies(section, policyType, rulesArray, casbinRules);
            await CasbinRules.AddRangeAsync(casbinRules);
            await DbContext.SaveChangesAsync();
        }

        #endregion

        #region Remove policy

        public virtual void RemovePolicy(string section, string policyType, IPolicyValues rule)
        {
            if (rule is null || rule.Count is 0)
            {
                return;
            }

            RemovePolicyInMemory(section, policyType, rule);
            DbContext.SaveChanges();
        }

        public virtual async Task RemovePolicyAsync(string section, string policyType, IPolicyValues rule)
        {
            if (rule is null || rule.Count is 0)
            {
                return;
            }

            RemovePolicyInMemory(section, policyType, rule);
            await DbContext.SaveChangesAsync();
        }

        public virtual void RemoveFilteredPolicy(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            if (fieldValues is null || fieldValues.Count is 0)
            {
                return;
            }

            RemoveFilteredPolicyInMemory(section, policyType, fieldIndex, fieldValues);
            DbContext.SaveChanges();
        }

        public virtual async Task RemoveFilteredPolicyAsync(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            if (fieldValues is null || fieldValues.Count is 0)
            {
                return;
            }

            RemoveFilteredPolicyInMemory(section, policyType, fieldIndex, fieldValues);
            await DbContext.SaveChangesAsync();
        }


        public virtual void RemovePolicies(string section, string policyType, IReadOnlyList<IPolicyValues> rules)
        {
            if (rules is null)
            {
                return;
            }

            var rulesArray = rules as IPolicyValues[] ?? rules.ToArray();
            if (rulesArray.Length is 0)
            {
                return;
            }

            foreach (var rule in rulesArray)
            {
                RemovePolicyInMemory(section, policyType, rule);
            }
            DbContext.SaveChanges();
        }

        public virtual async Task RemovePoliciesAsync(string section, string policyType, IReadOnlyList<IPolicyValues> rules)
        {
            if (rules is null)
            {
                return;
            }

            var rulesArray = rules as IPolicyValues[] ?? rules.ToArray();
            if (rulesArray.Length is 0)
            {
                return;
            }

            foreach (var rule in rulesArray)
            {
                RemovePolicyInMemory(section, policyType, rule);
            }
            await DbContext.SaveChangesAsync();
        }

        #endregion

        #region Update policy

        public virtual void UpdatePolicy(string section, string policyType, IPolicyValues oldRule, IPolicyValues newRule)
        {
            if (oldRule is null || oldRule.Count is 0 || newRule is null || newRule.Count is 0)
            {
                return;
            }

            var casbinRuleQuery = CasbinRules.ApplyQueryFilter(policyType, 0, oldRule);
            if(casbinRuleQuery.Count() == 0) return;
            var casbinRule = casbinRuleQuery.FirstOrDefault();
            casbinRule.SetValue(newRule);
            DbContext.SaveChanges();
        }

        public virtual async Task UpdatePolicyAsync(string section, string policyType, IPolicyValues oldRule, IPolicyValues newRule)
        {
            if (oldRule is null || oldRule.Count is 0 || newRule is null || newRule.Count is 0)
            {
                return;
            }

            var casbinRuleQuery = CasbinRules.ApplyQueryFilter(policyType, 0, oldRule);
            if(casbinRuleQuery.Count() == 0) return;
            var casbinRule = await casbinRuleQuery.FirstOrDefaultAsync();
            casbinRule.SetValue(newRule);
            await DbContext.SaveChangesAsync();
        }

        public virtual void UpdatePolicies(string section, string policyType, 
            IReadOnlyList<IPolicyValues> oldRules, IReadOnlyList<IPolicyValues> newRules)
        {
            if (oldRules is null || newRules is null || newRules.Count  != oldRules.Count)
            {
                return;
            }

            for (int i = 0; i < oldRules.Count; i++)
            {
                UpdatePolicy(section, policyType, oldRules[i], newRules[i]);
            }
        }

        public virtual async Task UpdatePoliciesAsync(string section, string policyType, 
            IReadOnlyList<IPolicyValues> oldRules, IReadOnlyList<IPolicyValues> newRules)
        {
            if (oldRules is null || newRules is null || newRules.Count  != oldRules.Count)
            {
                return;
            }

            for (int i = 0; i < oldRules.Count; i++)
            {
                UpdatePolicyAsync(section, policyType, oldRules[i], newRules[i]);
            }
        }

        #endregion

        #region IFilteredAdapter

        public bool IsFiltered { get; private set; }

        public void LoadFilteredPolicy(IPolicyStore model, Filter filter)
        {
            var casbinRules = CasbinRules.AsNoTracking()
                .ApplyQueryFilter(filter);
            casbinRules = OnLoadPolicy(model, casbinRules);
            model.LoadPolicyFromCasbinRules(casbinRules.ToList());
            IsFiltered = true;
        }

        public async Task LoadFilteredPolicyAsync(IPolicyStore model, Filter filter)
        {
            var casbinRules = CasbinRules.AsNoTracking()
                .ApplyQueryFilter(filter);
            casbinRules = OnLoadPolicy(model, casbinRules);
            model.LoadPolicyFromCasbinRules(await casbinRules.ToListAsync());
            IsFiltered = true;
        }

        #endregion

        private void RemovePolicyInMemory(string section, string policyType, IPolicyValues rule)
        {
            RemoveFilteredPolicy(section, policyType, 0, rule);
        }

        private void RemoveFilteredPolicyInMemory(string section, string policyType, int fieldIndex, IPolicyValues fieldValues)
        {
            var query = CasbinRules.ApplyQueryFilter(policyType, fieldIndex, fieldValues);

            query = OnRemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues, query);
            CasbinRules.RemoveRange(query);
        }

    }
}
