using System.Linq;
using System.Threading.Tasks;
using Casbin.Model;
using Casbin.Persist.Adapter.EFCore.Entities;
using Casbin.Persist.Adapter.EFCore.UnitTest.Extensions;
using Casbin.Persist.Adapter.EFCore.UnitTest.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Casbin.Persist.Adapter.EFCore.UnitTest
{
    /// <summary>
    /// Tests to ensure backward compatibility with existing single-context behavior.
    /// These tests verify that the multi-context changes don't break existing usage patterns.
    /// </summary>
    public class BackwardCompatibilityTest : TestUtil,
        IClassFixture<ModelProvideFixture>,
        IClassFixture<DbContextProviderFixture>
    {
        private readonly ModelProvideFixture _modelProvideFixture;
        private readonly DbContextProviderFixture _dbContextProviderFixture;

        public BackwardCompatibilityTest(
            ModelProvideFixture modelProvideFixture,
            DbContextProviderFixture dbContextProviderFixture)
        {
            _modelProvideFixture = modelProvideFixture;
            _dbContextProviderFixture = dbContextProviderFixture;
        }

        [Fact]
        public void TestSingleContextConstructorStillWorks()
        {
            // Arrange - Using original constructor pattern
            using var context = _dbContextProviderFixture.GetContext<int>("SingleContextConstructor");
            context.Clear();

            // Act - Create adapter using single-context constructor (original API)
            var adapter = new EFCoreAdapter<int>(context);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Add policies
            enforcer.AddPolicy("alice", "data1", "read");
            enforcer.AddGroupingPolicy("alice", "admin");

            // Assert - All policies should be in single context
            Assert.Equal(2, context.Policies.Count());

            var policies = context.Policies.ToList();
            Assert.Contains(policies, p => p.Type == "p" && p.Value1 == "alice");
            Assert.Contains(policies, p => p.Type == "g" && p.Value1 == "alice");
        }

        [Fact]
        public async Task TestSingleContextAsyncOperationsStillWork()
        {
            // Arrange
            await using var context = _dbContextProviderFixture.GetContext<int>("SingleContextAsync");
            context.Clear();

            var adapter = new EFCoreAdapter<int>(context);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act
            await enforcer.AddPolicyAsync("alice", "data1", "read");
            await enforcer.AddGroupingPolicyAsync("alice", "admin");

            // Assert
            Assert.Equal(2, await context.Policies.CountAsync());
        }

        [Fact]
        public void TestSingleContextLoadAndSave()
        {
            // Arrange
            using var context = _dbContextProviderFixture.GetContext<int>("SingleContextLoadSave");
            context.Clear();

            var adapter = new EFCoreAdapter<int>(context);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act - Add and save
            enforcer.AddPolicy("alice", "data1", "read");
            enforcer.AddGroupingPolicy("alice", "admin");
            enforcer.SavePolicy();

            // Create new enforcer and load
            var newEnforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);
            newEnforcer.LoadPolicy();

            // Assert
            TestGetPolicy(newEnforcer, AsList(
                AsList("alice", "data1", "read")
            ));

            TestGetGroupingPolicy(newEnforcer, AsList(
                AsList("alice", "admin")
            ));
        }

        [Fact]
        public void TestSingleContextWithExistingTests()
        {
            // This test mimics the pattern from EFCoreAdapterTest.cs to ensure compatibility
            using var context = _dbContextProviderFixture.GetContext<int>("ExistingPattern");
            context.Clear();

            // Initialize with data (like InitPolicy in EFCoreAdapterTest.cs)
            context.Policies.AddRange(new[]
            {
                new EFCorePersistPolicy<int> { Type = "p", Value1 = "alice", Value2 = "data1", Value3 = "read" },
                new EFCorePersistPolicy<int> { Type = "p", Value1 = "bob", Value2 = "data2", Value3 = "write" },
                new EFCorePersistPolicy<int> { Type = "g", Value1 = "alice", Value2 = "data2_admin" }
            });
            context.SaveChanges();

            var adapter = new EFCoreAdapter<int>(context);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act - Load policy
            enforcer.LoadPolicy();

            // Assert - Should match expected behavior
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            ));

            TestGetGroupingPolicy(enforcer, AsList(
                AsList("alice", "data2_admin")
            ));
        }

        [Fact]
        public void TestSingleContextRemoveOperations()
        {
            // Arrange
            using var context = _dbContextProviderFixture.GetContext<int>("SingleContextRemove");
            context.Clear();

            var adapter = new EFCoreAdapter<int>(context);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            enforcer.AddPolicy("alice", "data1", "read");
            enforcer.AddPolicy("bob", "data2", "write");

            // Act
            enforcer.RemovePolicy("alice", "data1", "read");

            // Assert
            Assert.Single(context.Policies);
            var remaining = context.Policies.First();
            Assert.Equal("bob", remaining.Value1);
        }

        [Fact]
        public void TestSingleContextUpdateOperations()
        {
            // Arrange
            using var context = _dbContextProviderFixture.GetContext<int>("SingleContextUpdate");
            context.Clear();

            var adapter = new EFCoreAdapter<int>(context);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            enforcer.AddPolicy("alice", "data1", "read");

            // Act
            enforcer.UpdatePolicy(
                AsList("alice", "data1", "read"),
                AsList("alice", "data1", "write")
            );

            // Assert
            Assert.Single(context.Policies);
            var policy = context.Policies.First();
            Assert.Equal("write", policy.Value3);
        }

        [Fact]
        public void TestSingleContextBatchOperations()
        {
            // Arrange
            using var context = _dbContextProviderFixture.GetContext<int>("SingleContextBatch");
            context.Clear();

            var adapter = new EFCoreAdapter<int>(context);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act - Add multiple
            enforcer.AddPolicies(new[]
            {
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("charlie", "data3", "read")
            });

            // Assert
            Assert.Equal(3, context.Policies.Count());

            // Act - Remove multiple
            enforcer.RemovePolicies(new[]
            {
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            });

            // Assert
            Assert.Single(context.Policies);
        }

        [Fact]
        public void TestSingleContextFilteredLoading()
        {
            // Arrange
            using var context = _dbContextProviderFixture.GetContext<int>("SingleContextFiltered");
            context.Clear();

            context.Policies.AddRange(new[]
            {
                new EFCorePersistPolicy<int> { Type = "p", Value1 = "alice", Value2 = "data1", Value3 = "read" },
                new EFCorePersistPolicy<int> { Type = "p", Value1 = "bob", Value2 = "data2", Value3 = "write" },
                new EFCorePersistPolicy<int> { Type = "g", Value1 = "alice", Value2 = "admin" }
            });
            context.SaveChanges();

            var adapter = new EFCoreAdapter<int>(context);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act - Load only alice's policies
            enforcer.LoadFilteredPolicy(new SimpleFieldFilter("p", 0, Policy.ValuesFrom(AsList("alice", "", ""))));

            // Assert
            Assert.True(adapter.IsFiltered);
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read")
            ));
        }

        [Fact]
        public void TestSingleContextProviderWrapping()
        {
            // Arrange - Create adapter with explicit SingleContextProvider
            using var context = _dbContextProviderFixture.GetContext<int>("ProviderWrapping");
            context.Clear();

            var provider = new SingleContextProvider<int>(context);
            var adapter = new EFCoreAdapter<int>(provider);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            // Act
            enforcer.AddPolicy("alice", "data1", "read");

            // Assert - Should behave identically to direct context constructor
            Assert.Single(context.Policies);
            Assert.Equal("alice", context.Policies.First().Value1);
        }

        [Fact]
        public void TestSingleContextProviderGetAllContexts()
        {
            // Arrange
            using var context = _dbContextProviderFixture.GetContext<int>("ProviderGetAll");
            var provider = new SingleContextProvider<int>(context);

            // Act
            var contexts = provider.GetAllContexts().ToList();

            // Assert
            Assert.Single(contexts);
            Assert.Same(context, contexts[0]);
        }

        [Fact]
        public void TestSingleContextProviderGetContextForPolicyType()
        {
            // Arrange
            using var context = _dbContextProviderFixture.GetContext<int>("ProviderGetForType");
            var provider = new SingleContextProvider<int>(context);

            // Act & Assert - All policy types should return same context
            Assert.Same(context, provider.GetContextForPolicyType("p"));
            Assert.Same(context, provider.GetContextForPolicyType("p2"));
            Assert.Same(context, provider.GetContextForPolicyType("g"));
            Assert.Same(context, provider.GetContextForPolicyType("g2"));
            Assert.Same(context, provider.GetContextForPolicyType(null));
            Assert.Same(context, provider.GetContextForPolicyType(""));
        }
    }
}
