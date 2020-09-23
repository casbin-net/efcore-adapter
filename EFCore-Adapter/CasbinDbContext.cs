using Microsoft.EntityFrameworkCore;
using System;

namespace Casbin.NET.Adapter.EFCore
{
    public partial class CasbinDbContext<TKey> : DbContext where TKey : IEquatable<TKey>
    {
        public virtual DbSet<CasbinRule<TKey>> CasbinRule { get; set; }

        private readonly IEntityTypeConfiguration<CasbinRule<TKey>> _casbinModelConfig;

        public CasbinDbContext()
        {
        }

        public CasbinDbContext(DbContextOptions<CasbinDbContext<TKey>> options) : base(options)
        {
        }

        public CasbinDbContext(DbContextOptions<CasbinDbContext<TKey>> options, IEntityTypeConfiguration<CasbinRule<TKey>> casbinModelConfig) : base(options)
        {
            _casbinModelConfig = casbinModelConfig;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (_casbinModelConfig != null)
            {
                modelBuilder.ApplyConfiguration(_casbinModelConfig);
            }
        }

    }
}
