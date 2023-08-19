using System;

namespace Casbin.Persist.Adapter.EFCore.UnitTest.Extensions
{
    public static class CasbinDbContextExtension
    {
        internal static void Clear<TKey>(this CasbinDbContext<TKey> dbContext) where TKey : IEquatable<TKey>
        {
            dbContext.RemoveRange(dbContext.Policies);
            dbContext.SaveChanges();
        }
    }
}