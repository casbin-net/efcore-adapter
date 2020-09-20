using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NetCasbin.Model;
using NetCasbin.Persist;

namespace Casbin.NET.Adapter.EFCore
{
    public static class CasbinModelExtension
    {
        internal static void LoadPolicyFromCasbinRules<TCasbinRule>(this Model casbinModel, IEnumerable<TCasbinRule> rules) 
            where TCasbinRule : class, ICasbinRule
        {
            foreach (TCasbinRule rule in rules)
            {
                string ruleString = rule.ConvertToString();
                Helper.LoadPolicyLine(ruleString, casbinModel);
            }
        }

    }
}