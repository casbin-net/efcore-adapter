using System;
using Microsoft.EntityFrameworkCore;

namespace Casbin.Persist.Adapter.EFCore.UnitTest.Fixtures
{
    public class DbContextProviderFixture
    {
        public CasbinDbContext<TKey> GetContext<TKey>(string name) where TKey : IEquatable<TKey>
        {
            var options = new DbContextOptionsBuilder<CasbinDbContext<TKey>>()
                .UseSqlite($"Data Source={name}.db")
                .Options;
            var context = new CasbinDbContext<TKey>(options);

            // Ensure database and tables are created
            context.Database.EnsureCreated();

            // Force model to be initialized by accessing a property
            // This ensures the DbSet is properly configured
            _ = context.Model;

            return context;
        }
    }
}