using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin.Adapter.EFCore.Entities;
using Casbin.Adapter.EFCore.UnitTest.Extensions;
using Casbin.Adapter.EFCore.UnitTest.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Casbin;
using Casbin.Persist;

namespace Casbin.Adapter.EFCore.UnitTest
{
    public class AdapterTest : TestUtil, IClassFixture<ModelProvideFixture>, IClassFixture<DbContextProviderFixture>
    {
        private readonly ModelProvideFixture _modelProvideFixture;
        private readonly DbContextProviderFixture _dbContextProviderFixture;

        public AdapterTest(ModelProvideFixture modelProvideFixture, DbContextProviderFixture dbContextProviderFixture)
        {
            _modelProvideFixture = modelProvideFixture;
            _dbContextProviderFixture = dbContextProviderFixture;
        }

        [Fact]
        public void TestAdapterAutoSave()
        {
            using var context = _dbContextProviderFixture.GetContext<int>("AutoSave");
            InitPolicy(context);
            var adapter = new EFCoreAdapter<int>(context);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            #region Load policy test
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write")
            ));
            Assert.True(context.CasbinRule.AsNoTracking().Count() is 5);
            #endregion

            #region Add policy test
            enforcer.AddPolicy("alice", "data1", "write");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write"),
                AsList("alice", "data1", "write")
            ));
            Assert.True(context.CasbinRule.AsNoTracking().Count() is 6);
            #endregion

            #region Remove poliy test
            enforcer.RemovePolicy("alice", "data1", "write");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write")
            ));
            Assert.True(context.CasbinRule.AsNoTracking().Count() is 5);

            enforcer.RemoveFilteredPolicy(0, "data2_admin");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            ));
            Assert.True(context.CasbinRule.AsNoTracking().Count() is 3);
            #endregion

            #region Batch APIs test
            enforcer.AddPolicies(new []
            {
                new List<string>{"alice", "data2", "write"},
                new List<string>{"bob", "data1", "read"}
            });
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("alice", "data2", "write"),
                AsList("bob", "data1", "read")
            ));
            Assert.True(context.CasbinRule.AsNoTracking().Count() is 5);

            enforcer.RemovePolicies(new []
            {
                new List<string>{"alice", "data1", "read"},
                new List<string>{"bob", "data2", "write"}
            });
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data2", "write"),
                AsList("bob", "data1", "read")
            ));
            Assert.True(context.CasbinRule.AsNoTracking().Count() is 3);
            #endregion

            #region IFilteredAdapter test
            enforcer.LoadFilteredPolicy(new Filter
            {
                P = new List<string>{"bob", "data1", "read"},
            });
            TestGetPolicy(enforcer, AsList(
                AsList("bob", "data1", "read")
            ));
            Assert.True(enforcer.Model.Sections["g"]["g"].Policy.Count is 0);
            Assert.True(context.CasbinRule.AsNoTracking().Count() is 3);

            enforcer.LoadFilteredPolicy(new Filter
            {
                P = new List<string>{"", "data2", ""},
                G = new List<string>{"", "data2_admin"},
            });
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data2", "write")
            ));
            TestGetGroupingPolicy(enforcer, AsList(
                AsList("alice", "data2_admin")
            ));
            Assert.True(enforcer.Model.Sections["g"]["g"].Policy.Count is 1);
            Assert.True(context.CasbinRule.AsNoTracking().Count() is 3);
            #endregion
        }

        [Fact]
        public async Task TestAdapterAutoSaveAsync()
        {
            await using var context = _dbContextProviderFixture.GetContext<int>("AutoSaveAsync");
            InitPolicy(context);
            var adapter = new EFCoreAdapter<int>(context);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            #region Load policy test
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write")
            ));
            Assert.True(context.CasbinRule.AsNoTracking().Count() is 5);
            #endregion

            #region Add policy test
            await enforcer.AddPolicyAsync("alice", "data1", "write");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write"),
                AsList("alice", "data1", "write")
            ));
            Assert.True(context.CasbinRule.AsNoTracking().Count() is 6);
            #endregion

            #region Remove policy test
            await enforcer.RemovePolicyAsync("alice", "data1", "write");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write")
            ));
            Assert.True(context.CasbinRule.AsNoTracking().Count() is 5);

            await enforcer.RemoveFilteredPolicyAsync(0, "data2_admin");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            ));
            Assert.True(context.CasbinRule.AsNoTracking().Count() is 3);
            #endregion

            #region Batch APIs test
            await enforcer.AddPoliciesAsync(new []
            {
                new List<string>{"alice", "data2", "write"},
                new List<string>{"bob", "data1", "read"}
            });
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("alice", "data2", "write"),
                AsList("bob", "data1", "read")
            ));
            Assert.True(context.CasbinRule.AsNoTracking().Count() is 5);

            await enforcer.RemovePoliciesAsync(new []
            {
                new List<string>{"alice", "data1", "read"},
                new List<string>{"bob", "data2", "write"}
            });
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data2", "write"),
                AsList("bob", "data1", "read")
            ));
            Assert.True(context.CasbinRule.AsNoTracking().Count() is 3);
            #endregion

            #region IFilteredAdapter test
            await enforcer.LoadFilteredPolicyAsync(new Filter
            {
                P = new List<string>{"bob", "data1", "read"},
            });
            TestGetPolicy(enforcer, AsList(
                AsList("bob", "data1", "read")
            ));
            Assert.True(enforcer.Model.Sections["g"]["g"].Policy.Count is 0);
            Assert.True(context.CasbinRule.AsNoTracking().Count() is 3);

            await enforcer.LoadFilteredPolicyAsync(new Filter
            {
                P = new List<string>{"", "data2", ""},
                G = new List<string>{"", "data2_admin"},
            });
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data2", "write")
            ));
            TestGetGroupingPolicy(enforcer, AsList(
                AsList("alice", "data2_admin")
            ));
            Assert.True(enforcer.Model.Sections["g"]["g"].Policy.Count is 1);
            Assert.True(context.CasbinRule.AsNoTracking().Count() is 3);
            #endregion
        }
        
        private static void InitPolicy(CasbinDbContext<int> context)
        {
            context.Clear();
            context.CasbinRule.Add(new CasbinRule<int>
            {
                PType = "p",
                V0 = "alice",
                V1 = "data1",
                V2 = "read",
            });
            context.CasbinRule.Add(new CasbinRule<int>
            {
                PType = "p",
                V0 = "bob",
                V1 = "data2",
                V2 = "write",
            });
            context.CasbinRule.Add(new CasbinRule<int>
            {
                PType = "p",
                V0 = "data2_admin",
                V1 = "data2",
                V2 = "read",
            });
            context.CasbinRule.Add(new CasbinRule<int>
            {
                PType = "p",
                V0 = "data2_admin",
                V1 = "data2",
                V2 = "write",
            });
            context.CasbinRule.Add(new CasbinRule<int>
            {
                PType = "g",
                V0 = "alice",
                V1 = "data2_admin",
            });
            context.SaveChanges();
        }
    }
}
