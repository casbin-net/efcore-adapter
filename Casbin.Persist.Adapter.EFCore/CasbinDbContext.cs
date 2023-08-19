using System;
using Casbin.Persist.Adapter.EFCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Casbin.Persist.Adapter.EFCore
{
    public partial class CasbinDbContext<TKey> : DbContext where TKey : IEquatable<TKey>
    {
        public virtual DbSet<EFCorePersistPolicy<TKey>> Policies { get; set; }

        private readonly IEntityTypeConfiguration<EFCorePersistPolicy<TKey>> _casbinModelConfig;
        private readonly string _defaultSchemaName;

        public CasbinDbContext()
        {
        }

        public CasbinDbContext(DbContextOptions<CasbinDbContext<TKey>> options, string defaultSchemaName = null, string defaultTableName = "casbin_rule") : base(options)
        {
            _casbinModelConfig = new DefaultPersistPolicyEntityTypeConfiguration<TKey>(defaultTableName);
            _defaultSchemaName = defaultSchemaName;
        }

        public CasbinDbContext(DbContextOptions<CasbinDbContext<TKey>> options, IEntityTypeConfiguration<EFCorePersistPolicy<TKey>> casbinModelConfig, string defaultSchemaName = null) : base(options)
        {
            _casbinModelConfig = casbinModelConfig;
            _defaultSchemaName = defaultSchemaName;
        }

        protected CasbinDbContext(DbContextOptions options, string defaultSchemaName = null, string defaultTableName = "casbin_rule") : base(options)
        {
            _casbinModelConfig = new DefaultPersistPolicyEntityTypeConfiguration<TKey>(defaultTableName);
            _defaultSchemaName = defaultSchemaName;
        }

        protected CasbinDbContext(DbContextOptions options, IEntityTypeConfiguration<EFCorePersistPolicy<TKey>> casbinModelConfig, string defaultSchemaName = null) : base(options)
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
