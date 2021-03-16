using Casbin.Adapter.EFCore.Entities;
using Microsoft.EntityFrameworkCore;
using System;

namespace Casbin.Adapter.EFCore
{
    public partial class CasbinDbContext<TKey> : DbContext where TKey : IEquatable<TKey>
    {
        public virtual DbSet<CasbinRule<TKey>> CasbinRule { get; set; }

        private readonly IEntityTypeConfiguration<CasbinRule<TKey>> _casbinModelConfig;
        private readonly string _defaultSchemaName;

        public CasbinDbContext()
        {
        }

        public CasbinDbContext(DbContextOptions<CasbinDbContext<TKey>> options, string defaultSchemaName = null) : base(options)
        {
            _casbinModelConfig = new DefaultCasbinRuleEntityTypeConfiguration<TKey>();
            _defaultSchemaName = defaultSchemaName;
        }

        public CasbinDbContext(DbContextOptions<CasbinDbContext<TKey>> options, IEntityTypeConfiguration<CasbinRule<TKey>> casbinModelConfig, string defaultSchemaName = null) : base(options)
        {
            _casbinModelConfig = casbinModelConfig;
            _defaultSchemaName = defaultSchemaName;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (string.IsNullOrWhiteSpace(_defaultSchemaName) is false)
            {
                modelBuilder.HasDefaultSchema(_defaultSchemaName);
            }

            if (_casbinModelConfig is not null)
            {
                modelBuilder.ApplyConfiguration(_casbinModelConfig);
            }
        }

    }
}
