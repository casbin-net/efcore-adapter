using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin.Persist.Adapter.EFCore.Entities;
using Casbin.Persist.Adapter.EFCore.UnitTest.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Casbin.Persist.Adapter.EFCore.UnitTest
{
#if NET5_0_OR_GREATER
    // Custom DbContext class for factory testing
    public class TestCasbinDbContext : CasbinDbContext<int>
    {
        public TestCasbinDbContext(DbContextOptions<TestCasbinDbContext> options) : base(options)
        {
        }
    }

    public class DbContextFactoryTest : TestUtil, IClassFixture<ModelProvideFixture>
    {
        private readonly ModelProvideFixture _modelProvideFixture;

        public DbContextFactoryTest(ModelProvideFixture modelProvideFixture)
        {
            _modelProvideFixture = modelProvideFixture;
        }

        [Fact]
        public void TestDbContextFactoryBasicUsage()
        {
            // Setup DI container with DbContextFactory
            var services = new ServiceCollection();
            services.AddDbContextFactory<TestCasbinDbContext>(options =>
            {
                options.UseSqlite("Data Source=FactoryTest1.db");
            });
            
            var serviceProvider = services.BuildServiceProvider();
            
            // Get the factory and create an adapter
            var factory = serviceProvider.GetRequiredService<IDbContextFactory<TestCasbinDbContext>>();
            
            // Ensure database is created
            using (var context = factory.CreateDbContext())
            {
                context.Database.EnsureDeleted();
                context.Database.EnsureCreated();
            }
            
            var adapter = new EFCoreAdapter<int, EFCorePersistPolicy<int>, TestCasbinDbContext>(factory);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);
            
            // Add some policies
            enforcer.AddPolicy("alice", "data1", "read");
            enforcer.AddPolicy("bob", "data2", "write");
            
            // Verify policies were saved
            Assert.True(enforcer.Enforce("alice", "data1", "read"));
            Assert.True(enforcer.Enforce("bob", "data2", "write"));
            Assert.False(enforcer.Enforce("alice", "data2", "read"));
            
            // Create a new enforcer to verify persistence
            var adapter2 = new EFCoreAdapter<int, EFCorePersistPolicy<int>, TestCasbinDbContext>(factory);
            var enforcer2 = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter2);
            enforcer2.LoadPolicy();
            
            // Verify policies were loaded
            Assert.True(enforcer2.Enforce("alice", "data1", "read"));
            Assert.True(enforcer2.Enforce("bob", "data2", "write"));
            
            TestGetPolicy(enforcer2, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            ));
            
            // Cleanup
            using (var context = factory.CreateDbContext())
            {
                context.Database.EnsureDeleted();
            }
        }

        [Fact]
        public async Task TestDbContextFactoryAsyncOperations()
        {
            // Setup DI container with DbContextFactory
            var services = new ServiceCollection();
            services.AddDbContextFactory<TestCasbinDbContext>(options =>
            {
                options.UseSqlite("Data Source=FactoryTest2.db");
            });
            
            var serviceProvider = services.BuildServiceProvider();
            
            // Get the factory and create an adapter
            var factory = serviceProvider.GetRequiredService<IDbContextFactory<TestCasbinDbContext>>();
            
            // Ensure database is created
            using (var context = factory.CreateDbContext())
            {
                context.Database.EnsureDeleted();
                context.Database.EnsureCreated();
            }
            
            var adapter = new EFCoreAdapter<int, EFCorePersistPolicy<int>, TestCasbinDbContext>(factory);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);
            
            // Add some policies asynchronously
            await enforcer.AddPolicyAsync("alice", "data1", "read");
            await enforcer.AddPolicyAsync("bob", "data2", "write");
            await enforcer.AddPoliciesAsync(new []
            {
                new List<string>{"alice", "data2", "write"},
                new List<string>{"bob", "data1", "read"}
            });
            
            // Verify policies were saved
            Assert.True(enforcer.Enforce("alice", "data1", "read"));
            Assert.True(enforcer.Enforce("bob", "data2", "write"));
            Assert.True(enforcer.Enforce("alice", "data2", "write"));
            Assert.True(enforcer.Enforce("bob", "data1", "read"));
            
            // Remove a policy
            await enforcer.RemovePolicyAsync("alice", "data2", "write");
            Assert.False(enforcer.Enforce("alice", "data2", "write"));
            
            // Create a new enforcer to verify persistence
            var adapter2 = new EFCoreAdapter<int, EFCorePersistPolicy<int>, TestCasbinDbContext>(factory);
            var enforcer2 = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter2);
            await enforcer2.LoadPolicyAsync();
            
            // Verify policies were loaded correctly
            Assert.True(enforcer2.Enforce("alice", "data1", "read"));
            Assert.True(enforcer2.Enforce("bob", "data2", "write"));
            Assert.False(enforcer2.Enforce("alice", "data2", "write"));
            Assert.True(enforcer2.Enforce("bob", "data1", "read"));
            
            TestGetPolicy(enforcer2, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("bob", "data1", "read")
            ));
            
            // Cleanup
            using (var context = factory.CreateDbContext())
            {
                context.Database.EnsureDeleted();
            }
        }

        [Fact]
        public void TestDbContextFactorySimulatesDIScenario()
        {
            // This test simulates the real DI scenario where the enforcer
            // outlives the individual DbContext instances
            
            var services = new ServiceCollection();
            services.AddDbContextFactory<TestCasbinDbContext>(options =>
            {
                options.UseSqlite("Data Source=FactoryTest3.db");
            });
            
            var serviceProvider = services.BuildServiceProvider();
            var factory = serviceProvider.GetRequiredService<IDbContextFactory<TestCasbinDbContext>>();
            
            // Ensure database is created
            using (var context = factory.CreateDbContext())
            {
                context.Database.EnsureDeleted();
                context.Database.EnsureCreated();
            }
            
            // Create enforcer once (like in the DI scenario)
            var adapter = new EFCoreAdapter<int, EFCorePersistPolicy<int>, TestCasbinDbContext>(factory);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);
            
            // Simulate multiple requests - each operation should create/dispose its own context
            for (int i = 0; i < 5; i++)
            {
                enforcer.AddPolicy($"user{i}", "data1", "read");
            }
            
            // Load policy - should work even after multiple operations
            enforcer.LoadPolicy();
            
            // Verify all policies are still there
            for (int i = 0; i < 5; i++)
            {
                Assert.True(enforcer.Enforce($"user{i}", "data1", "read"));
            }
            
            // Remove some policies
            enforcer.RemovePolicy("user0", "data1", "read");
            enforcer.RemovePolicy("user1", "data1", "read");
            
            // Verify removal worked
            Assert.False(enforcer.Enforce("user0", "data1", "read"));
            Assert.False(enforcer.Enforce("user1", "data1", "read"));
            Assert.True(enforcer.Enforce("user2", "data1", "read"));
            
            // Reload to verify persistence
            enforcer.LoadPolicy();
            
            Assert.False(enforcer.Enforce("user0", "data1", "read"));
            Assert.False(enforcer.Enforce("user1", "data1", "read"));
            Assert.True(enforcer.Enforce("user2", "data1", "read"));
            Assert.True(enforcer.Enforce("user3", "data1", "read"));
            Assert.True(enforcer.Enforce("user4", "data1", "read"));
            
            // Cleanup
            using (var context = factory.CreateDbContext())
            {
                context.Database.EnsureDeleted();
            }
        }
    }
#endif
}
