using Casbin.Adapter.EFCore.UnitTest.Extensions;
using Casbin.Adapter.EFCore.UnitTest.Fixtures;
using Casbin;
using Casbin.Model;
using Xunit;

namespace Casbin.Adapter.EFCore.UnitTest
{
    public class SpecialPolicyTest :  IClassFixture<ModelProvideFixture>, IClassFixture<DbContextProviderFixture>
    {
        private readonly ModelProvideFixture _modelProvideFixture;
        private readonly DbContextProviderFixture _dbContextProviderFixture;

        public SpecialPolicyTest(ModelProvideFixture modelProvideFixture, DbContextProviderFixture dbContextProviderFixture)
        {
            _modelProvideFixture = modelProvideFixture;
            _dbContextProviderFixture = dbContextProviderFixture;
        }

        [Fact]
        public void TestCommaPolicy()
        {
            var context = _dbContextProviderFixture.GetContext<int>("CommaPolicy");
            context.Clear();
            var adapter = new EFCoreAdapter<int>(context);
            var enforcer = new Enforcer(DefaultModel.CreateFromText(@"
[request_definition]
r = _

[policy_definition]
p = rule, a1, a2

[policy_effect]
e = some(where (p.eft == allow))

[matchers]
m = eval(p.rule)
"), adapter);
            enforcer.AddFunction("equal", (a1 , a2) => a1 == a2);
            
            enforcer.AddPolicy("equal(p.a1, p.a2)", "a1", "a1");
            Assert.True(enforcer.Enforce("_"));
            
            enforcer.LoadPolicy();
            Assert.True(enforcer.Enforce("_"));
            
            enforcer.RemovePolicy("equal(p.a1, p.a2)", "a1", "a1");
            enforcer.AddPolicy("equal(p.a1, p.a2)", "a1", "a2");
            Assert.False(enforcer.Enforce("_"));
            
            enforcer.LoadPolicy();
            Assert.False(enforcer.Enforce("_"));
        }
    }
}