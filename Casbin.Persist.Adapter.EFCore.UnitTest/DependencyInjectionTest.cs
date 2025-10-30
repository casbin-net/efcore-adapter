using Microsoft.Extensions.DependencyInjection;
using Casbin.Persist.Adapter.EFCore.UnitTest.Fixtures;
using Xunit;
using Casbin.Model;

namespace Casbin.Persist.Adapter.EFCore.UnitTest
{
    public class DependencyInjectionTest : IClassFixture<TestHostFixture>, IClassFixture<ModelProvideFixture>
    {
        private readonly TestHostFixture _testHostFixture;
        private readonly ModelProvideFixture _modelProvideFixture;

        public DependencyInjectionTest(TestHostFixture testHostFixture, ModelProvideFixture modelProvideFixture)
        {
            _testHostFixture = testHostFixture;
            _modelProvideFixture = modelProvideFixture;
        }

        [Fact]
        public void ShouldResolveCasbinDbContext()
        {
            var dbContext = _testHostFixture.Services.GetService<CasbinDbContext<int>>();
            Assert.NotNull(dbContext);
            dbContext.Database.EnsureCreated();
        }

        [Fact]
        public void ShouldResolveEfCoreAdapter()
        {
            var adapter = _testHostFixture.Services.GetService<IAdapter>();
            Assert.NotNull(adapter);
        }

        [Fact]
        public void ShouldUseAdapterAcrossMultipleScopesWithDbContextDirectly()
        {
            // Simulate the issue where an adapter is created in one scope
            // but used in another scope (like with casbin-aspnetcore)
            IAdapter adapter;
            
            // Create adapter with DbContext in first scope
            using (var scope1 = _testHostFixture.Services.CreateScope())
            {
                var dbContext = scope1.ServiceProvider.GetRequiredService<CasbinDbContext<int>>();
                dbContext.Database.EnsureCreated();
                adapter = new EFCoreAdapter<int>(dbContext);
            }
            
            // Try to use adapter after scope is disposed - this should throw ObjectDisposedException
            var model = _modelProvideFixture.GetNewRbacModel();
            Assert.Throws<System.ObjectDisposedException>(() => adapter.LoadPolicy(model));
        }

        [Fact]
        public void ShouldUseAdapterAcrossMultipleScopesWithServiceProvider()
        {
            // Create adapter with IServiceProvider - this should work across multiple scopes
            var adapter = new EFCoreAdapter<int>(_testHostFixture.Services);
            
            // Ensure database is created in first scope
            using (var scope1 = _testHostFixture.Services.CreateScope())
            {
                var dbContext = scope1.ServiceProvider.GetRequiredService<CasbinDbContext<int>>();
                dbContext.Database.EnsureCreated();
            }
            
            // Use adapter after scope is disposed - this should work with IServiceProvider
            var model = _modelProvideFixture.GetNewRbacModel();
            adapter.LoadPolicy(model); // Should not throw
        }
    }
}