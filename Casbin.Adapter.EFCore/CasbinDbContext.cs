using Casbin.Adapter.EFCore.Entities;
using Microsoft.EntityFrameworkCore;
using System;

namespace Casbin.Adapter.EFCore
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
            _casbinModelConfig = new DefaultCasbinRuleEntityTypeConfiguration<TKey>();
        }

        public CasbinDbContext(DbContextOptions<CasbinDbContext<TKey>> options, IEntityTypeConfiguration<CasbinRule<TKey>> casbinModelConfig) : base(options)
        {
            _casbinModelConfig = casbinModelConfig;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (_casbinModelConfig is not null)
            {
                modelBuilder.ApplyConfiguration(_casbinModelConfig);
            }
        }

    }
}
