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

    public class CasbinDbAdapter<TKey, TCasbinRule> : IAdapter 
        where TCasbinRule : class, ICasbinRule<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        protected DbContext DbContext { get; }
        protected DbSet<TCasbinRule> CasbinRules  { get; }

        public CasbinDbAdapter(DbContext context)
        {
            DbContext = context ?? throw new ArgumentNullException(nameof(context));
            CasbinRules = DbContext.Set<TCasbinRule>();
        }

        #region Load policy

        public virtual void LoadPolicy(Model model)
        {
            var rules = CasbinRules.AsNoTracking().ToList();
            model.LoadPolicyFromCasbinRules(rules);
        }

        public virtual async Task LoadPolicyAsync(Model model)
        {
            var rules = await CasbinRules.AsNoTracking().ToListAsync();
            model.LoadPolicyFromCasbinRules(rules);
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

            CasbinRules.AddRange(casbinRules);
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

            await CasbinRules.AddRangeAsync(casbinRules);
            await DbContext.SaveChangesAsync();
        }

        #endregion

        #region Add policy

        public virtual void AddPolicy(string sec, string ptype, IList<string> rule)
        {
            var casbinRule = CasbinRuleExtenstion.Parse<TCasbinRule>(ptype, rule);
            CasbinRules.Add(casbinRule);
            DbContext.SaveChanges();
        }

        public virtual async Task AddPolicyAsync(string sec, string ptype, IList<string> rule)
        {
            var casbinRule = CasbinRuleExtenstion.Parse<TCasbinRule>(ptype, rule);
            await CasbinRules.AddAsync(casbinRule);
            await DbContext.SaveChangesAsync();
        }

        #endregion

        #region Remove policy

        public virtual void RemovePolicy(string sec, string ptype, IList<string> rule)
        {
            RemoveFilteredPolicy(sec, ptype, 0, rule.ToArray());
        }

        public virtual async Task RemovePolicyAsync(string sec, string ptype, IList<string> rule)
        {
            await RemoveFilteredPolicyAsync(sec, ptype, 0, rule.ToArray());
        }

        public virtual void RemoveFilteredPolicy(string sec, string ptype, int fieldIndex, params string[] fieldValues)
        {
            if (fieldValues is null || fieldValues.Length is 0)
            {
                return;
            }

            var query = CasbinRules
                .Where(p => p.PType == ptype)
                .ApplyQueryFilter(fieldIndex, fieldValues);

            CasbinRules.RemoveRange(query);
            DbContext.SaveChanges();
        }

        public virtual async Task RemoveFilteredPolicyAsync(string sec, string ptype, int fieldIndex, params string[] fieldValues)
        {
            if (fieldValues is null || fieldValues.Length is 0)
            {
                return;
            }

            var query = CasbinRules
                .Where(p => p.PType == ptype)
                .ApplyQueryFilter(fieldIndex, fieldValues);

            CasbinRules.RemoveRange(query);
            await DbContext.SaveChangesAsync();
        }

        #endregion
    }
}
