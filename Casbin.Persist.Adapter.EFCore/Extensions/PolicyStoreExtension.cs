using System.Collections.Generic;
using Casbin.Model;

namespace Casbin.Persist.Adapter.EFCore.Extensions
{
    public static class PolicyStoreExtension
    {
        internal static void LoadPolicyFromPersistPolicy<TPersistPolicy>(this IPolicyStore store, IEnumerable<TPersistPolicy> persistPolicies) 
            where TPersistPolicy : class, IPersistPolicy
        {
            foreach (var policy in persistPolicies)
            {
                if (string.IsNullOrWhiteSpace(policy.Section))
                {
                    policy.Section = policy.Type.Substring(0, 1);
                }
                var requiredCount = store.GetRequiredValuesCount(policy.Section, policy.Type);
                var values = Policy.ValuesFrom(policy, requiredCount);
                store.AddPolicy(policy.Section, policy.Type, values);
            }
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
    }
}