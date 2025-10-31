using System;
using System.Linq;

namespace Casbin.Persist.Adapter.EFCore.UnitTest.Extensions
{
    public static class CasbinDbContextExtension
    {
        internal static void Clear<TKey>(this CasbinDbContext<TKey> dbContext) where TKey : IEquatable<TKey>
        {
            // Force model initialization before ensuring database exists
            // This ensures EF Core knows about all entity configurations
            _ = dbContext.Model;

            // Ensure database and tables exist before attempting to clear
            dbContext.Database.EnsureCreated();

            // Try to access and clear policies
            try
            {
                var policies = dbContext.Policies.ToList();
                if (policies.Count > 0)
                {
                    dbContext.RemoveRange(policies);
                    dbContext.SaveChanges();
                }
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                // If table still doesn't exist after EnsureCreated,
                // force a second attempt with model refresh
                dbContext.Database.EnsureDeleted();
                _ = dbContext.Model;
                dbContext.Database.EnsureCreated();
            }
        }
    }
}