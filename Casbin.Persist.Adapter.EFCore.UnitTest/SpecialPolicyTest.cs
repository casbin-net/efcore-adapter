using Casbin.Persist.Adapter.EFCore.UnitTest.Extensions;
using Casbin.Model;
using Casbin.Persist.Adapter.EFCore.Entities;
using Casbin.Persist.Adapter.EFCore.UnitTest.Fixtures;
using Xunit;
using System;

namespace Casbin.Persist.Adapter.EFCore.UnitTest
{
    public class SpecialPolicyTest : TestUtil, IClassFixture<ModelProvideFixture>,
        IClassFixture<DbContextProviderFixture>
    {
        private readonly ModelProvideFixture _modelProvideFixture;
        private readonly DbContextProviderFixture _dbContextProviderFixture;

        public SpecialPolicyTest(ModelProvideFixture modelProvideFixture,
            DbContextProviderFixture dbContextProviderFixture)
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
            var enforcer = new Enforcer(DefaultModel.CreateFromText(
                """
                    [request_definition]
                    r = _
                
                    [policy_definition]
                    p = rule, a1, a2
                
                    [policy_effect]
                    e = some(where (p.eft == allow))
                
                    [matchers]
                    m = eval(p.rule)
                """
            ), adapter);

            enforcer.AddFunction<Func<object, object, bool>>("equal", (a1, a2) => a1 == a2);
//            enforcer.AddFunction("equal", (a1, a2) => a1 == a2);

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

        [Fact]
        public void TestUnexpectedPolicy()
        {
            var context = _dbContextProviderFixture.GetContext<int>("UnexpectedPolicy");
            context.Clear();
            context.Policies.Add(new EFCorePersistPolicy<int>()
            {
                Type = "p",
                Value1 = "a1",
                Value2 = "a2",
                Value3 = null,
            });
            context.Policies.Add(new EFCorePersistPolicy<int>()
            {
                Type = "p",
                Value1 = "a1",
                Value2 = "a2",
                Value3 = "a3",
            });
            context.Policies.Add(new EFCorePersistPolicy<int>()
            {
                Type = "p",
                Value1 = "a1",
                Value2 = "a2",
                Value3 = "a3",
                Value4 = "a4",
            });
            context.Policies.Add(new EFCorePersistPolicy<int>()
            {
                Type = "p",
                Value1 = "b1",
                Value2 = "b2",
                Value3 = "b3",
                Value4 = "b4",
            });
            context.SaveChanges();

            var adapter = new EFCoreAdapter<int>(context);
            var enforcer = new Enforcer(DefaultModel.CreateFromText(
                """
                    [request_definition]
                    r = _
                
                    [policy_definition]
                    p = a1, a2, a3
                
                    [policy_effect]
                    e = some(where (p.eft == allow))
                
                    [matchers]
                    m = true
                """), adapter);

            enforcer.LoadPolicy();
            var policies = enforcer.GetPolicy();

            TestGetPolicy(enforcer, AsList(
                AsList("a1", "a2", ""),
                AsList("a1", "a2", "a3"),
                AsList("b1", "b2", "b3")
            ));
        }
    }
}