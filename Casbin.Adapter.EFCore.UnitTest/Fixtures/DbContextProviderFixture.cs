using System;
using Casbin.Adapter.EFCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Casbin.Adapter.EFCore.UnitTest.Fixtures
{
    public class DbContextProviderFixture
    {
        public CasbinDbContext<TKey> GetContext<TKey>(string name) where TKey : IEquatable<TKey>
        {
            var options = new DbContextOptionsBuilder<CasbinDbContext<TKey>>()
                .UseSqlite($"Data Source={name}.db")
                .Options;
            var context = new CasbinDbContext<TKey>(options);
            context.Database.EnsureCreated();
            return context;
        }
    }
}
