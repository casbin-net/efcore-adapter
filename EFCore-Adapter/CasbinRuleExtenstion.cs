using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NetCasbin.Model;

namespace Casbin.NET.Adapter.EFCore
{
    public static class CasbinRuleExtenstion
    {
        internal static string ConvertToString(this ICasbinRule rule)
        {
            var stringBuilder = new StringBuilder(rule.PType);
            AppendValue(stringBuilder, rule.V0);
            AppendValue(stringBuilder, rule.V1);
            AppendValue(stringBuilder, rule.V2);
            AppendValue(stringBuilder, rule.V3);
            AppendValue(stringBuilder, rule.V4);
            AppendValue(stringBuilder, rule.V5);
            return stringBuilder.ToString();
        }

        internal static void ReadPolicyFromCasbinModel<TCasbinRule>(this ICollection<TCasbinRule> casbinRules, Model casbinModel) 
            where TCasbinRule : class,ICasbinRule, new()
        {
            if (casbinModel.Model.ContainsKey("p"))
            {
                foreach (var assertionKeyValuePair in casbinModel.Model["p"])
                {
                    string policyType = assertionKeyValuePair.Key;
                    Assertion assertion = assertionKeyValuePair.Value;
                    foreach (TCasbinRule rule in assertion.Policy
                        .Select(ruleStrings => 
                            Parse<TCasbinRule>(policyType, ruleStrings)))
                    {
                        casbinRules.Add(rule);
                    }
                }
            }
            if (casbinModel.Model.ContainsKey("g"))
            {
                foreach (var assertionKeyValuePair in casbinModel.Model["g"])
                {
                    string policyType = assertionKeyValuePair.Key;
                    Assertion assertion = assertionKeyValuePair.Value;
                    foreach (TCasbinRule rule in assertion.Policy
                        .Select(ruleStrings => 
                            Parse<TCasbinRule>(policyType, ruleStrings)))
                    {
                        casbinRules.Add(rule);
                    }
                }
            }
        }

        internal static IQueryable<TCasbinRule> ApplyQueryFilter<TCasbinRule>(this IQueryable<TCasbinRule> query, int fieldIndex, IList<string> fieldValues)
            where TCasbinRule : ICasbinRule
        {
            if (fieldIndex > 5)
            {
                throw new ArgumentOutOfRangeException(nameof(fieldIndex));
            }

            int fieldValueCount = fieldValues.Count;

            if (fieldValueCount is 0)
            {
                return query;
            }

            int lastIndex = fieldIndex + fieldValueCount;

            if (lastIndex > 5)
            {
                throw new ArgumentOutOfRangeException(nameof(lastIndex));
            }

            if (fieldIndex is 0 && lastIndex > 0)
            {
                query = query.Where(p => p.V0 == fieldValues[0 - fieldIndex]);
            }

            if (fieldIndex <= 1 && lastIndex > 1)
            {
                query = query.Where(p => p.V1 == fieldValues[1 - fieldIndex]);
            }

            if (fieldIndex <= 2 && lastIndex > 2)
            {
                query = query.Where(p => p.V2 == fieldValues[2 - fieldIndex]);
            }

            if (fieldIndex <= 3 && lastIndex > 3)
            {
                query = query.Where(p => p.V3 == fieldValues[3 - fieldIndex]);
            }

            if (fieldIndex <= 4 && lastIndex > 4)
            {
                query = query.Where(p => p.V4 == fieldValues[4 - fieldIndex]);
            }

            if (lastIndex is 5)
            {
                query = query.Where(p => p.V5 == fieldValues[5 - fieldIndex]);
            }

            return query;
        }

        internal static TCasbinRule Parse<TCasbinRule>(string policyType, IList<string> ruleStrings)
            where TCasbinRule : ICasbinRule, new()
        {
            var rule = new TCasbinRule{PType = policyType};
            int count = ruleStrings.Count;

            if (count > 0)
            {
                rule.V0 = ruleStrings[0];
            }

            if (count > 1)
            {
                rule.V1 = ruleStrings[1];
            }

            if (count > 2)
            {
                rule.V2 = ruleStrings[2];
            }

            if (count > 3)
            {
                rule.V3 = ruleStrings[3];
            }

            if (count > 4)
            {
                rule.V4 = ruleStrings[4];
            }

            if (count > 5)
            {
                rule.V5 = ruleStrings[5];
            }

            return rule;
        }

        private static void AppendValue(StringBuilder stringBuilder, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }
            stringBuilder.Append(", ");
            stringBuilder.Append(value);
        }
    }
}