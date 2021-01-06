using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Casbin.NET.Adapter.EFCore;
using EFCore_Adapter.UnitTest.Fixtures;
using Microsoft.EntityFrameworkCore;
using NetCasbin;
using Xunit;

namespace EFCore_Adapter.UnitTest
{
    public class AdapterTest : TestUtil, IClassFixture<ModelProvideFixture>, IDisposable
    {
        private readonly ModelProvideFixture _modelProvideFixture;
        private readonly CasbinDbContext<int> _context;
        private readonly CasbinDbContext<int> _asyncContext;

        public AdapterTest(ModelProvideFixture modelProvideFixture)
        {
            _modelProvideFixture = modelProvideFixture;
            var options = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseSqlite("Data Source=casbin_test.sqlite3")
                .Options;

            var asyncOptions = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseSqlite("Data Source=casbin_async_test.sqlite3")
                .Options;

            _context = new CasbinDbContext<int>(options);
            _context.Database.EnsureCreated();
            _asyncContext = new CasbinDbContext<int>(asyncOptions);
            _asyncContext.Database.EnsureCreated();

            InitPolicy(_context);
            InitPolicy(_asyncContext);
        }

        public void Dispose()
        {
            Dispose(_context);
            Dispose(_asyncContext);
        }

        private void Dispose(CasbinDbContext<int> context)
        {
            context.RemoveRange(context.CasbinRule);
            context.SaveChanges();
        }

        private static void InitPolicy(CasbinDbContext<int> context)
        {
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

        [Fact]
        public void TestAdapterAutoSave()
        {
            var adapter = new CasbinDbAdapter<int>(_context);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write")
            ));
            Assert.True(_context.CasbinRule.AsNoTracking().Count() is 5);

            enforcer.AddPolicy("alice", "data1", "write");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write"),
                AsList("alice", "data1", "write")
            ));
            Assert.True(_context.CasbinRule.AsNoTracking().Count() is 6);

            enforcer.RemovePolicy("alice", "data1", "write");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write")
            ));
            Assert.True(_context.CasbinRule.AsNoTracking().Count() is 5);

            enforcer.RemoveFilteredPolicy(0, "data2_admin");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            ));
            Assert.True(_context.CasbinRule.AsNoTracking().Count() is 3);

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
            Assert.True(_context.CasbinRule.AsNoTracking().Count() is 5);

            
            enforcer.RemovePolicies(new []
            {
                new List<string>{"alice", "data1", "read"},
                new List<string>{"bob", "data2", "write"}
            });
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data2", "write"),
                AsList("bob", "data1", "read")
            ));
            Assert.True(_context.CasbinRule.AsNoTracking().Count() is 3);
        }

        [Fact]
        public async Task TestAdapterAutoSaveAsync()
        {
            var adapter = new CasbinDbAdapter<int, CasbinRule<int>>(_asyncContext);
            var enforcer = new Enforcer(_modelProvideFixture.GetNewRbacModel(), adapter);

            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write")
            ));
            Assert.True(_asyncContext.CasbinRule.AsNoTracking().Count() is 5);

            await enforcer.AddPolicyAsync("alice", "data1", "write");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write"),
                AsList("alice", "data1", "write")
            ));
            Assert.True(_asyncContext.CasbinRule.AsNoTracking().Count() is 6);

            await enforcer.RemovePolicyAsync("alice", "data1", "write");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write")
            ));
            Assert.True(_asyncContext.CasbinRule.AsNoTracking().Count() is 5);

            await enforcer.RemoveFilteredPolicyAsync(0, "data2_admin");
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            ));
            Assert.True(_asyncContext.CasbinRule.AsNoTracking().Count() is 3);

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
            Assert.True(_asyncContext.CasbinRule.AsNoTracking().Count() is 5);

            await enforcer.RemovePoliciesAsync(new []
            {
                new List<string>{"alice", "data1", "read"},
                new List<string>{"bob", "data2", "write"}
            });
            TestGetPolicy(enforcer, AsList(
                AsList("alice", "data2", "write"),
                AsList("bob", "data1", "read")
            ));
            Assert.True(_asyncContext.CasbinRule.AsNoTracking().Count() is 3);
        }
    }
}
