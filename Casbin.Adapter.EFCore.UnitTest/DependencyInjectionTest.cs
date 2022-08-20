using Casbin.Adapter.EFCore.UnitTest.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Casbin.Persist;

namespace Casbin.Adapter.EFCore.UnitTest
{
    public class DependencyInjectionTest : IClassFixture<TestHostFixture>
    {
        private readonly TestHostFixture _testHostFixture;

        public DependencyInjectionTest(TestHostFixture testHostFixture)
        {
            _testHostFixture = testHostFixture;
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
    }
}