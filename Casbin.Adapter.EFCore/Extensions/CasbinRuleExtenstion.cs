using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Casbin.Model;
using Casbin.Persist;

namespace Casbin.Adapter.EFCore.Extensions
{
    public static class CasbinRuleExtenstion
    {
        internal static List<string> ToList(this ICasbinRule rule)
        {
            var list = new List<string> {rule.PType};
            if (string.IsNullOrEmpty(rule.V0) is false)
            {
                list.Add(rule.V0);
            }
            if (string.IsNullOrEmpty(rule.V1) is false)
            {
                list.Add(rule.V1);
            }
            if (string.IsNullOrEmpty(rule.V2) is false)
            {
                list.Add(rule.V2);
            }
            if (string.IsNullOrEmpty(rule.V3) is false)
            {
                list.Add(rule.V3);
            }
            if (string.IsNullOrEmpty(rule.V4) is false)
            {
                list.Add(rule.V4);
            }
            if (string.IsNullOrEmpty(rule.V5) is false)
            {
                list.Add(rule.V5);
            }
            return list;
        }

        internal static void ReadPolicyFromCasbinModel<TPersistPolicy>(this ICollection<TPersistPolicy> persistPolicies, IPolicyStore store) 
            where TPersistPolicy : class, IPersistPolicy, new()
        {
            var types = store.GetPolicyTypesAllSections();
            foreach (var section in types)
            {
                foreach (var type in section.Value)
                {
                    var scanner = store.Scan(section.Key, type);
                    while (scanner.GetNext(out var values))
                    {
                        persistPolicies.Add(PersistPolicy.Create<TPersistPolicy>(section.Key, type, values));
                    }
                }
            }
        }

        internal static IQueryable<TCasbinRule> ApplyQueryFilter<TCasbinRule>(this IQueryable<TCasbinRule> query, 
            string policyType , int fieldIndex, IEnumerable<string> fieldValues)
            where TCasbinRule : ICasbinRule
        {
            if (fieldIndex > 5)
            {
                throw new ArgumentOutOfRangeException(nameof(fieldIndex));
            }
            var fieldValuesList = fieldValues as IList<string> ?? fieldValues.ToArray();
            int fieldValueCount = fieldValuesList.Count;

            if (fieldValueCount is 0)
            {
                return query;
            }

            int lastIndex = fieldIndex + fieldValueCount - 1;

            if (lastIndex > 5)
            {
                throw new ArgumentOutOfRangeException(nameof(lastIndex));
            }

            query = query.Where(p => string.Equals(p.PType, policyType));

            if (fieldIndex is 0 && lastIndex >= 0)
            {
                string field = fieldValuesList[fieldIndex];
                if (string.IsNullOrWhiteSpace(field) is false)
                {
                    query = query.Where(p => p.V0 == field);
                }
            }

            if (fieldIndex <= 1 && lastIndex >= 1)
            {
                string field = fieldValuesList[1 - fieldIndex];
                if (string.IsNullOrWhiteSpace(field) is false)
                {
                    query = query.Where(p => p.V1 == field);
                }
            }

            if (fieldIndex <= 2 && lastIndex >= 2)
            {
                string field = fieldValuesList[2 - fieldIndex];
                if (string.IsNullOrWhiteSpace(field) is false)
                {
                    query = query.Where(p => p.V2 == field);
                }
            }

            if (fieldIndex <= 3 && lastIndex >= 3)
            {
                string field = fieldValuesList[3 - fieldIndex];
                if (string.IsNullOrWhiteSpace(field) is false)
                {
                    query = query.Where(p => p.V3 == field);
                }
            }

            if (fieldIndex <= 4 && lastIndex >= 4)
            {
                string field = fieldValuesList[4 - fieldIndex];
                if (string.IsNullOrWhiteSpace(field) is false)
                {
                    query = query.Where(p => p.V4 == field);
                }
            }

            if (lastIndex is 5) // and fieldIndex <= 5
            {
                string field = fieldValuesList[5 - fieldIndex];
                if (string.IsNullOrWhiteSpace(field) is false)
                {
                    query = query.Where(p => p.V5 == field);
                }
            }

            return query;
        }

        internal static IQueryable<TCasbinRule> ApplyQueryFilter<TCasbinRule>(this IQueryable<TCasbinRule> query, Filter filter)
            where TCasbinRule : ICasbinRule
        {
            if (filter is null)
            {
                return query;
            }

            if (filter.P is null && filter.G is null)
            {
                return query;
            }

            if (filter.P is not null && filter.G is not null)
            {
                var queryP = query.ApplyQueryFilter(PermConstants.DefaultPolicyType, 0, filter.P);
                var queryG = query.ApplyQueryFilter(PermConstants.DefaultGroupingPolicyType, 0, filter.G);
                return queryP.Union(queryG);
            }
            if (filter.P is not null)
            {
                query = query.ApplyQueryFilter(PermConstants.DefaultPolicyType, 0, filter.P);
            }
            if (filter.G is not null)
            {
                query = query.ApplyQueryFilter(PermConstants.DefaultGroupingPolicyType, 0, filter.G);
            }
            return query;
        }

        internal static TCasbinRule Parse<TCasbinRule>(string policyType, IEnumerable<string> ruleStrings)
            where TCasbinRule : ICasbinRule, new()
        {
            var rule = new TCasbinRule{PType = policyType};
            var strings = ruleStrings.ToList();
            int count = strings.Count;
            if (count > 0)
            {
                rule.V0 = strings[0];
            }
            if (count > 1)
            {
                rule.V1 = strings[1];
            }
            if (count > 2)
            {
                rule.V2 = strings[2];
            }
            if (count > 3)
            {
                rule.V3 = strings[3];
            }
            if (count > 4)
            {
                rule.V4 = strings[4];
            }
            if (count > 5)
            {
                rule.V5 = strings[5];
            }
            return rule;
        }
    }
}