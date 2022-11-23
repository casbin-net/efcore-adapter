using System.Collections.Generic;
using Casbin.Model;
using Casbin.Persist;

namespace Casbin.Adapter.EFCore.Extensions
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
                store.AddPolicy(policy.Section, policy.Type, Policy.ValuesFrom(policy));
            }
        }
    }
}