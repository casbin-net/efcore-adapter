using NetCasbin;
using Xunit;
using Casbin.NET.Adapter.EFCore;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;

namespace EFCore_Adapter.Test
{
    public class AdapterTest : TestUtil, IDisposable
    {
        public CasbinDbContext<int> _context { get; set; }

        public AdapterTest()
        {
            var options = new DbContextOptionsBuilder<CasbinDbContext<int>>()
                .UseSqlite("Data Source=casbin_test.sqlite3")
                .Options;

            _context = new CasbinDbContext<int>(options);
            _context.Database.EnsureCreated();
            InitPolicy();
        }

        public void Dispose()
        {
            _context.RemoveRange(_context.CasbinRule);
            _context.SaveChanges();
        }

        private void InitPolicy()
        {
            _context.CasbinRule.Add(new CasbinRule<int>()
            {
                PType = "p",
                V0 = "alice",
                V1 = "data1",
                V2 = "read",
            });
            _context.CasbinRule.Add(new CasbinRule<int>()
            {
                PType = "p",
                V0 = "bob",
                V1 = "data2",
                V2 = "write",
            });
            _context.CasbinRule.Add(new CasbinRule<int>()
            {
                PType = "p",
                V0 = "data2_admin",
                V1 = "data2",
                V2 = "read",
            });
            _context.CasbinRule.Add(new CasbinRule<int>()
            {
                PType = "p",
                V0 = "data2_admin",
                V1 = "data2",
                V2 = "write",
            });
            _context.CasbinRule.Add(new CasbinRule<int>()
            {
                PType = "g",
                V0 = "alice",
                V1 = "data2_admin",
            });
            _context.SaveChanges();
        }

        [Fact]
        public void Test_Adapter_AutoSave()
        {
            var efAdapter = new CasbinDbAdapter<int>(_context);
            Enforcer e = new Enforcer("examples/rbac_model.conf", efAdapter);

            TestGetPolicy(e, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write")
            ));
            Assert.True(_context.CasbinRule.Count() == 5);

            e.AddPolicy("alice", "data1", "write");
            TestGetPolicy(e, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write"),
                AsList("alice", "data1", "write")
            ));
            Assert.True(_context.CasbinRule.Count() == 6);

            e.RemovePolicy("alice", "data1", "write");
            TestGetPolicy(e, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write"),
                AsList("data2_admin", "data2", "read"),
                AsList("data2_admin", "data2", "write")
            ));
            Assert.True(_context.CasbinRule.Count() == 5);

            e.RemoveFilteredPolicy(0, "data2_admin");
            TestGetPolicy(e, AsList(
                AsList("alice", "data1", "read"),
                AsList("bob", "data2", "write")
            ));
            Assert.True(_context.CasbinRule.Count() == 3);
        }
    }
}
