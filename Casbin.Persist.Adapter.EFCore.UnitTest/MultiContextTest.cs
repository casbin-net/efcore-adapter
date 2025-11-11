using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin.Persist.Adapter.EFCore.Entities;
using Casbin.Persist.Adapter.EFCore.UnitTest.Extensions;
using Casbin.Persist.Adapter.EFCore.UnitTest.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Casbin.Persist.Adapter.EFCore.UnitTest
{
    /// <summary>
    /// Tests for multi-context functionality where different policy types
    /// can be stored in separate database contexts/tables/schemas.
    /// </summary>
    public class MultiContextTest : TestUtil,
        IClassFixture<ModelProvideFixture>,
        IClassFixture<MultiContextProviderFixture>
    {
        private readonly ModelProvideFixture _modelProvideFixture;
        private readonly MultiContextProviderFixture _multiContextProviderFixture;

        public MultiContextTest(
            ModelProvideFixture modelProvideFixture,
            MultiContextProviderFixture multiContextProviderFixture)
        {
            _modelProvideFixture = modelProvideFixture;
            _multiContextProviderFixture = multiContextProviderFixture;
        }

        [Fact]
        public void TestMultiContextAddPolicy()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("AddPolicy");
            var (policyContext, groupingContext) = _multiContextProviderFixture.GetSeparateContexts("AddPolicy");

            policyContext.Clear();
            groupingContext.Clear();

            var adapter = new EFCoreAdapter<int>(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act - Add policy rules (should go to policy context)
            enforcer.AddPolicy("alice", "data1", "read");
            enforcer.AddPolicy("bob", "data2", "write");

            // Add grouping rules (should go to grouping context)
            enforcer.AddGroupingPolicy("alice", "admin");

            // Assert - Verify policies are in the correct contexts
            Assert.Equal(2, policyContext.Policies.Count());
            Assert.Equal(1, groupingContext.Policies.Count());

            // Verify policy data
            var alicePolicy = policyContext.Policies.FirstOrDefault(p => p.Value1 == "alice");
            Assert.NotNull(alicePolicy);
            Assert.Equal("p", alicePolicy.Type);
            Assert.Equal("data1", alicePolicy.Value2);
            Assert.Equal("read", alicePolicy.Value3);

            // Verify grouping data
            var aliceGrouping = groupingContext.Policies.FirstOrDefault(p => p.Value1 == "alice");
            Assert.NotNull(aliceGrouping);
            Assert.Equal("g", aliceGrouping.Type);
            Assert.Equal("admin", aliceGrouping.Value2);
        }

        [Fact]
        public async Task TestMultiContextAddPolicyAsync()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("AddPolicyAsync");
            var (policyContext, groupingContext) = _multiContextProviderFixture.GetSeparateContexts("AddPolicyAsync");

            policyContext.Clear();
            groupingContext.Clear();

            var adapter = new EFCoreAdapter<int>(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act
            await enforcer.AddPolicyAsync("alice", "data1", "read");
            await enforcer.AddPolicyAsync("bob", "data2", "write");
            await enforcer.AddGroupingPolicyAsync("alice", "admin");

            // Assert
            Assert.Equal(2, await policyContext.Policies.CountAsync());
            Assert.Equal(1, await groupingContext.Policies.CountAsync());
        }

        [Fact]
        public void TestMultiContextRemovePolicy()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("RemovePolicy");
            var (policyContext, groupingContext) = _multiContextProviderFixture.GetSeparateContexts("RemovePolicy");

            policyContext.Clear();
            groupingContext.Clear();

            // Pre-populate data
            policyContext.Policies.Add(new EFCorePersistPolicy<int>
            {
                Type = "p",
                Value1 = "alice",
                Value2 = "data1",
                Value3 = "read"
            });
            policyContext.SaveChanges();

            groupingContext.Policies.Add(new EFCorePersistPolicy<int>
            {
                Type = "g",
                Value1 = "alice",
                Value2 = "admin"
            });
            groupingContext.SaveChanges();

            var adapter = new EFCoreAdapter<int>(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);
            enforcer.LoadPolicy();

            // Act
            enforcer.RemovePolicy("alice", "data1", "read");
            enforcer.RemoveGroupingPolicy("alice", "admin");

            // Assert
            Assert.Equal(0, policyContext.Policies.Count());
            Assert.Equal(0, groupingContext.Policies.Count());
        }

        [Fact]
        public void TestMultiContextLoadPolicy()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("LoadPolicy");
            var (policyContext, groupingContext) = _multiContextProviderFixture.GetSeparateContexts("LoadPolicy");

            policyContext.Clear();
            groupingContext.Clear();

            // Add test data to policy context
            policyContext.Policies.AddRange(new[]
            {
                new EFCorePersistPolicy<int> { Type = "p", Value1 = "alice", Value2 = "data1", Value3 = "read" },
                new EFCorePersistPolicy<int> { Type = "p", Value1 = "bob", Value2 = "data2", Value3 = "write" }
            });
            policyContext.SaveChanges();

            // Add test data to grouping context
            groupingContext.Policies.AddRange(new[]
            {
                new EFCorePersistPolicy<int> { Type = "g", Value1 = "alice", Value2 = "admin" },
                new EFCorePersistPolicy<int> { Type = "g", Value1 = "bob", Value2 = "user" }
            });
            groupingContext.SaveChanges();

            var adapter = new EFCoreAdapter<int>(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act
            enforcer.LoadPolicy();

            // Assert - Verify all policies loaded from both contexts
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            ));

            TestGetGroupingPolicy(enforcer, AsList(
                AsList("alice", "admin"),
                AsList("bob", "user")
            ));
        }

        [Fact]
        public async Task TestMultiContextLoadPolicyAsync()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("LoadPolicyAsync");
            var (policyContext, groupingContext) = _multiContextProviderFixture.GetSeparateContexts("LoadPolicyAsync");

            policyContext.Clear();
            groupingContext.Clear();

            policyContext.Policies.AddRange(new[]
            {
                new EFCorePersistPolicy<int> { Type = "p", Value1 = "alice", Value2 = "data1", Value3 = "read" }
            });
            await policyContext.SaveChangesAsync();

            groupingContext.Policies.Add(new EFCorePersistPolicy<int> { Type = "g", Value1 = "alice", Value2 = "admin" });
            await groupingContext.SaveChangesAsync();

            var adapter = new EFCoreAdapter<int>(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act
            await enforcer.LoadPolicyAsync();

            // Assert
            Assert.Single(enforcer.GetPolicy());
            Assert.Single(enforcer.GetGroupingPolicy());
        }

        [Fact]
        public void TestMultiContextSavePolicy()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("SavePolicy");
            var (policyContext, groupingContext) = _multiContextProviderFixture.GetSeparateContexts("SavePolicy");

            policyContext.Clear();
            groupingContext.Clear();

            var adapter = new EFCoreAdapter<int>(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Add policies via enforcer
            enforcer.AddPolicy("alice", "data1", "read");
            enforcer.AddPolicy("bob", "data2", "write");
            enforcer.AddGroupingPolicy("alice", "admin");

            // Act - Save should distribute policies to correct contexts
            enforcer.SavePolicy();

            // Assert - Verify data is in correct contexts
            Assert.Equal(2, policyContext.Policies.Count());
            Assert.Equal(1, groupingContext.Policies.Count());

            // Verify we can reload from both contexts
            var newEnforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);
            newEnforcer.LoadPolicy();

            TestGetPolicy(newEnforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            ));

            TestGetGroupingPolicy(newEnforcer, AsList(
                AsList("alice", "admin")
            ));
        }

        [Fact]
        public async Task TestMultiContextSavePolicyAsync()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("SavePolicyAsync");
            var (policyContext, groupingContext) = _multiContextProviderFixture.GetSeparateContexts("SavePolicyAsync");

            policyContext.Clear();
            groupingContext.Clear();

            var adapter = new EFCoreAdapter<int>(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            enforcer.AddPolicy("alice", "data1", "read");
            enforcer.AddGroupingPolicy("alice", "admin");

            // Act
            await enforcer.SavePolicyAsync();

            // Assert
            Assert.Equal(1, await policyContext.Policies.CountAsync());
            Assert.Equal(1, await groupingContext.Policies.CountAsync());
        }

        [Fact]
        public void TestMultiContextBatchOperations()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("BatchOperations");
            var (policyContext, groupingContext) = _multiContextProviderFixture.GetSeparateContexts("BatchOperations");

            policyContext.Clear();
            groupingContext.Clear();

            var adapter = new EFCoreAdapter<int>(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act - Add multiple policies at once
            enforcer.AddPolicies(new[]
            {
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("charlie", "data3", "read")
            });

            // Assert
            Assert.Equal(3, policyContext.Policies.Count());

            // Act - Remove multiple policies
            enforcer.RemovePolicies(new[]
            {
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            });

            // Assert
            Assert.Equal(1, policyContext.Policies.Count());
            Assert.Equal("charlie", policyContext.Policies.First().Value1);
        }

        [Fact]
        public void TestMultiContextLoadFilteredPolicy()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("LoadFilteredPolicy");
            var (policyContext, groupingContext) = _multiContextProviderFixture.GetSeparateContexts("LoadFilteredPolicy");

            policyContext.Clear();
            groupingContext.Clear();

            // Add multiple policies
            policyContext.Policies.AddRange(new[]
            {
                new EFCorePersistPolicy<int> { Type = "p", Value1 = "alice", Value2 = "data1", Value3 = "read" },
                new EFCorePersistPolicy<int> { Type = "p", Value1 = "bob", Value2 = "data2", Value3 = "write" }
            });
            policyContext.SaveChanges();

            groupingContext.Policies.Add(new EFCorePersistPolicy<int> { Type = "g", Value1 = "alice", Value2 = "admin" });
            groupingContext.SaveChanges();

            var adapter = new EFCoreAdapter<int>(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act - Load only alice's policies
            enforcer.LoadFilteredPolicy(new Filter
            {
                P = AsList("alice", "", "")
            });

            // Assert
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read")
            ));

            // Bob's policy should not be loaded
            Assert.DoesNotContain(enforcer.GetPolicy(), p => p.Contains("bob"));
        }

        /// <summary>
        /// Verifies that UpdatePolicy operations work across multiple contexts without throwing exceptions.
        ///
        /// NOTE: This is NOT a transaction rollback test. This test uses separate SQLite database files
        /// (policy.db and grouping.db), making atomic cross-context transactions impossible.
        ///
        /// For actual transaction integrity and rollback verification across multiple contexts,
        /// see Integration/TransactionIntegrityTests.cs (PostgreSQL tests with shared connections).
        /// </summary>
        [Fact]
        public void TestMultiContextUpdatePolicyNoException()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("UpdatePolicyNoException");
            var (policyContext, groupingContext) = _multiContextProviderFixture.GetSeparateContexts("UpdatePolicyNoException");

            policyContext.Clear();
            groupingContext.Clear();

            var adapter = new EFCoreAdapter<int>(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Add initial data
            enforcer.AddPolicy("alice", "data1", "read");
            enforcer.AddGroupingPolicy("alice", "admin");

            var initialPolicyCount = policyContext.Policies.Count();
            var initialGroupingCount = groupingContext.Policies.Count();

            // Act & Assert - UpdatePolicy should complete without throwing exceptions
            enforcer.UpdatePolicy(
                AsList("alice", "data1", "read"),
                AsList("alice", "data1", "write")
            );

            // Verify the update was applied successfully
            Assert.True(enforcer.HasPolicy("alice", "data1", "write"));
            Assert.False(enforcer.HasPolicy("alice", "data1", "read"));
        }

        [Fact]
        public void TestMultiContextProviderGetAllContexts()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("GetAllContexts");

            // Act
            var contexts = provider.GetAllContexts().ToList();

            // Assert
            Assert.Equal(2, contexts.Count);
            Assert.All(contexts, ctx => Assert.NotNull(ctx));
        }

        [Fact]
        public void TestMultiContextProviderGetContextForPolicyType()
        {
            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("GetContextForType");

            // Act & Assert
            var pContext = provider.GetContextForPolicyType("p");
            var p2Context = provider.GetContextForPolicyType("p2");
            var gContext = provider.GetContextForPolicyType("g");
            var g2Context = provider.GetContextForPolicyType("g2");

            // All 'p' types should route to same context
            Assert.Same(pContext, p2Context);

            // All 'g' types should route to same context
            Assert.Same(gContext, g2Context);

            // 'p' and 'g' types should route to different contexts
            Assert.NotSame(pContext, gContext);
        }

        [Fact]
        public void TestDbSetCachingByPolicyType()
        {
            // This test verifies that the DbSet cache uses (context, policyType) as the composite key
            // rather than just context. This prevents the bug where different policy types would
            // incorrectly share the same cached DbSet.

            // Arrange
            var provider = _multiContextProviderFixture.GetMultiContextProvider("DbSetCaching");
            var (policyContext, groupingContext) = _multiContextProviderFixture.GetSeparateContexts("DbSetCaching");

            policyContext.Clear();
            groupingContext.Clear();

            // Create a custom adapter that tracks GetCasbinRuleDbSet calls
            var callTracker = new Dictionary<string, int>();
            var adapter = new DbSetCachingTestAdapter(provider, callTracker);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act - Add policies of different types
            enforcer.AddPolicy("alice", "data1", "read");      // Type 'p' - first call should invoke GetCasbinRuleDbSet
            enforcer.AddPolicy("bob", "data2", "write");       // Type 'p' - should use cached DbSet
            enforcer.AddGroupingPolicy("alice", "admin");      // Type 'g' - different type, should invoke GetCasbinRuleDbSet
            enforcer.AddGroupingPolicy("bob", "user");         // Type 'g' - should use cached DbSet

            // Assert - Verify GetCasbinRuleDbSet was called once per unique (context, policyType) combination
            // If the cache key was only 'context', it would be called once and return wrong DbSet for 'g'
            Assert.Equal(1, callTracker["p"]);  // Called once for 'p', then cached
            Assert.Equal(1, callTracker["g"]);  // Called once for 'g', then cached

            // Verify data went to correct contexts
            Assert.Equal(2, policyContext.Policies.Count());
            Assert.Equal(2, groupingContext.Policies.Count());

            // Verify policy types are correct
            Assert.All(policyContext.Policies, p => Assert.Equal("p", p.Type));
            Assert.All(groupingContext.Policies, g => Assert.Equal("g", g.Type));
        }
    }

    /// <summary>
    /// Test adapter that tracks how many times GetCasbinRuleDbSet is called per policy type.
    /// This is used to verify the DbSet caching behavior.
    /// </summary>
    internal class DbSetCachingTestAdapter : EFCoreAdapter<int>
    {
        private readonly Dictionary<string, int> _callTracker;

        public DbSetCachingTestAdapter(
            ICasbinDbContextProvider<int> contextProvider,
            Dictionary<string, int> callTracker)
            : base(contextProvider)
        {
            _callTracker = callTracker;
        }

        protected override DbSet<EFCorePersistPolicy<int>> GetCasbinRuleDbSet(DbContext dbContext, string policyType)
        {
            // Track that this method was called for this policy type
            // Only track non-null policy types (null is used for general operations)
            if (policyType != null)
            {
                if (!_callTracker.ContainsKey(policyType))
                {
                    _callTracker[policyType] = 0;
                }
                _callTracker[policyType]++;
            }

            // Call base implementation to get the actual DbSet
            return base.GetCasbinRuleDbSet(dbContext, policyType);
        }
    }
}
