using System;
using System.Linq;

namespace Casbin.Persist.Adapter.EFCore.UnitTest.Extensions
{
    public static class CasbinDbContextExtension
    {
        internal static void Clear<TKey>(this CasbinDbContext<TKey> dbContext) where TKey : IEquatable<TKey>
        {
            // Ensure database and tables exist before attempting to clear
            dbContext.Database.EnsureCreated();

            // Only remove and save if there are policies to clear
            var policies = dbContext.Policies.ToList();
            if (policies.Count > 0)
            {
                dbContext.RemoveRange(policies);
                dbContext.SaveChanges();
            }
        }
    }
}