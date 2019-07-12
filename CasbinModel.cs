using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace Casbin.NET.Adapter.EFCore
{
    public class CasbinDbContext : DbContext
    {
        public DbSet<CasbinRule> CasbinRule { get; set; }

        public CasbinDbContext()
        {
        }

        public CasbinDbContext(DbContextOptions<CasbinDbContext> options) : base(options)
        {
        }

    }

    public class CasbinRule
    {
        public int Id { get; set; }
        public string PType { get; set; }
        public string V0 { get; set; }
        public string V1 { get; set; }
        public string V2 { get; set; }
        public string V3 { get; set; }
        public string V4 { get; set; }
        public string V5 { get; set; }

    }
}
