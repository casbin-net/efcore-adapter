using NetCasbin.Persist;
using NetCasbin.Model;
using System.Collections.Generic;
using System.Linq;
using System;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace Casbin.NET.Adapter.EFCore
{
    public class CasbinDbAdapter<TKey> : CasbinDbAdapter<TKey, CasbinRule<TKey>> where TKey : IEquatable<TKey>
    {
        public CasbinDbAdapter(DbContext context) : base(context)
        {

        }
    }

    public class CasbinDbAdapter<TKey, TCasbinRule> : CasbinDbAdapter<TKey, TCasbinRule, DbContext> 
        where TCasbinRule : class, ICasbinRule<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        public CasbinDbAdapter(DbContext context) : base(context)
        {

        }
    }

    public class CasbinDbAdapter<TKey, TCasbinRule, TDbContext> : IAdapter 
        where TDbContext : DbContext
        where TCasbinRule : class, ICasbinRule<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        protected TDbContext DbContext { get; }
        protected DbSet<TCasbinRule> CasbinRules => _casbinRules ??= GetCasbinRuleDbSet(DbContext);
        private DbSet<TCasbinRule> _casbinRules;

        public CasbinDbAdapter(TDbContext context)
        {
            DbContext = context ?? throw new ArgumentNullException(nameof(context));
        }

        #region virtual method

        protected virtual DbSet<TCasbinRule> GetCasbinRuleDbSet(TDbContext dbContext)
        {
            return dbContext.Set<TCasbinRule>();
        }

        protected virtual IQueryable<TCasbinRule> OnLoadPolicy(Model model, IQueryable<TCasbinRule> casbinRules)
        {
            return casbinRules;
        }

        protected virtual IEnumerable<TCasbinRule> OnSavePolicy(Model model, IEnumerable<TCasbinRule> casbinRules)
        {
            return casbinRules;
        }

        protected virtual TCasbinRule OnAddPolicy(string section, string policyType, IEnumerable<string> rule, TCasbinRule casbinRules)
        {
            return casbinRules;
        }

        protected virtual IEnumerable<TCasbinRule> OnAddPolicies(string section, string policyType,
            IEnumerable<IEnumerable<string>> rules, IEnumerable<TCasbinRule> casbinRules)
        {
            return casbinRules;
        }

        protected virtual IQueryable<TCasbinRule> OnRemoveFilteredPolicy(string section, string policyType, int fieldIndex, string[] fieldValues, IQueryable<TCasbinRule> casbinRules)
        {
            return casbinRules;
        }

        #endregion

        #region Load policy

        public virtual void LoadPolicy(Model model)
        {
            var casbinRules = CasbinRules.AsNoTracking();
            casbinRules = OnLoadPolicy(model, casbinRules);
            model.LoadPolicyFromCasbinRules(casbinRules.ToList());
        }

        public virtual async Task LoadPolicyAsync(Model model)
        {
            var casbinRules = CasbinRules.AsNoTracking();
            casbinRules = OnLoadPolicy(model, casbinRules);
            model.LoadPolicyFromCasbinRules(await casbinRules.ToListAsync());
        }

        #endregion

        #region Save policy

        public virtual void SavePolicy(Model model)
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

        public virtual async Task SavePolicyAsync(Model model)
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

        public virtual void AddPolicy(string section, string policyType, IList<string> rule)
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

        public virtual async Task AddPolicyAsync(string section, string policyType, IList<string> rule)
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
        
        public void AddPolicies(string section, string policyType, IEnumerable<IList<string>> rules)
        {
            if (rules is null)
            {
                return;
            }

            var rulesArray = rules as IList<string>[] ?? rules.ToArray();
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

        public async Task AddPoliciesAsync(string section, string policyType, IEnumerable<IList<string>> rules)
        {
            if (rules is null)
            {
                return;
            }

            var rulesArray = rules as IList<string>[] ?? rules.ToArray();
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

        public virtual void RemovePolicy(string section, string policyType, IList<string> rule)
        {
            if (rule is null || rule.Count is 0)
            {
                return;
            }

            RemovePolicyInMemory(section, policyType, rule);
            DbContext.SaveChanges();
        }

        public virtual async Task RemovePolicyAsync(string section, string policyType, IList<string> rule)
        {
            if (rule is null || rule.Count is 0)
            {
                return;
            }

            RemovePolicyInMemory(section, policyType, rule);
            await DbContext.SaveChangesAsync();
        }

        public virtual void RemoveFilteredPolicy(string section, string policyType, int fieldIndex, params string[] fieldValues)
        {
            if (fieldValues is null || fieldValues.Length is 0)
            {
                return;
            }

            RemoveFilteredPolicyInMemory(section, policyType, fieldIndex, fieldValues);
            DbContext.SaveChanges();
        }

        public virtual async Task RemoveFilteredPolicyAsync(string section, string policyType, int fieldIndex, params string[] fieldValues)
        {
            if (fieldValues is null || fieldValues.Length is 0)
            {
                return;
            }

            RemoveFilteredPolicyInMemory(section, policyType, fieldIndex, fieldValues);
            await DbContext.SaveChangesAsync();
        }


        public void RemovePolicies(string section, string policyType, IEnumerable<IList<string>> rules)
        {
            if (rules is null)
            {
                return;
            }

            var rulesArray = rules as IList<string>[] ?? rules.ToArray();
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

        public async Task RemovePoliciesAsync(string section, string policyType, IEnumerable<IList<string>> rules)
        {
            if (rules is null)
            {
                return;
            }

            var rulesArray = rules as IList<string>[] ?? rules.ToArray();
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

        private void RemovePolicyInMemory(string section, string policyType, IEnumerable<string> rule)
        {
            RemoveFilteredPolicy(section, policyType, 0, rule as string[] ?? rule.ToArray());
        }

        private void RemoveFilteredPolicyInMemory(string section, string policyType, int fieldIndex, params string[] fieldValues)
        {
            var query = CasbinRules
                .Where(p => string.Equals(p.PType, policyType))
                .ApplyQueryFilter(fieldIndex, fieldValues);

            query = OnRemoveFilteredPolicy(section, policyType, fieldIndex, fieldValues, query);
            CasbinRules.RemoveRange(query);
        }
    }
}
