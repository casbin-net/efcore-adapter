using System.Collections.Generic;
using Casbin.Model;
using Casbin.Persist;

namespace Casbin.Adapter.EFCore.Extensions
{
    public static class CasbinModelExtension
    {
        internal static void LoadPolicyFromCasbinRules<TCasbinRule>(this IPolicyStore casbinModel, IEnumerable<TCasbinRule> rules) 
            where TCasbinRule : class, ICasbinRule
        {
            foreach (var rule in rules)
            {
                casbinModel.TryLoadPolicyLine(rule.ToList());
            }
        }
    }
}