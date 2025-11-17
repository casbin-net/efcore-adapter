using System.Linq;
using Casbin.Model;
using Casbin.Persist;

#nullable enable

namespace Casbin.Persist.Adapter.EFCore.UnitTest.Fixtures
{
    /// <summary>
    /// Simple field-based policy filter for testing.
    /// Replaces the deprecated Filter class for basic field filtering scenarios.
    /// </summary>
    public class SimpleFieldFilter : IPolicyFilter
    {
        private readonly PolicyFilter _policyFilter;

        /// <summary>
        /// Creates a filter that filters policies of the specified type by field values.
        /// </summary>
        /// <param name="policyType">The policy type to filter (e.g., "p", "g", "g2")</param>
        /// <param name="fieldIndex">The field index to start filtering from (usually 0)</param>
        /// <param name="values">The field values to filter by</param>
        public SimpleFieldFilter(string policyType, int fieldIndex, IPolicyValues values)
        {
            _policyFilter = new PolicyFilter(policyType, fieldIndex, values);
        }

        /// <summary>
        /// Applies the filter to the policy collection.
        /// </summary>
        public IQueryable<T> Apply<T>(IQueryable<T> policies) where T : IPersistPolicy
        {
            return _policyFilter.Apply(policies);
        }
    }
}
