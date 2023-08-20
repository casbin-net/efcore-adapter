using System;
using Casbin.Persist.Adapter.EFCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Casbin.Persist.Adapter.EFCore
{
    public partial class CasbinDbContext<TKey> : DbContext where TKey : IEquatable<TKey>
    {
        public virtual DbSet<EFCorePersistPolicy<TKey>> Policies { get; set; }
        private const string DefaultTableName = "casbin_rule";

        private readonly IEntityTypeConfiguration<EFCorePersistPolicy<TKey>> _casbinModelConfig;
        private readonly string _schemaName;

        public CasbinDbContext()
        {
            _casbinModelConfig = new DefaultPersistPolicyEntityTypeConfiguration<TKey>(DefaultTableName);
        }

        public CasbinDbContext(DbContextOptions<CasbinDbContext<TKey>> options, string schemaName = null, string tableName = DefaultTableName) : base(options)
        {
            _casbinModelConfig = new DefaultPersistPolicyEntityTypeConfiguration<TKey>(tableName);
            _schemaName = schemaName;
        }
        
        protected CasbinDbContext(DbContextOptions options, string schemaName = null, string tableName = DefaultTableName) : base(options)
        {
            _casbinModelConfig = new DefaultPersistPolicyEntityTypeConfiguration<TKey>(tableName);
            _schemaName = schemaName;
        }

        public CasbinDbContext(DbContextOptions<CasbinDbContext<TKey>> options, IEntityTypeConfiguration<EFCorePersistPolicy<TKey>> casbinModelConfig, string schemaName = null) : base(options)
        {
            _casbinModelConfig = casbinModelConfig;
            _schemaName = schemaName;
        }
        
        protected CasbinDbContext(DbContextOptions options, IEntityTypeConfiguration<EFCorePersistPolicy<TKey>> casbinModelConfig, string schemaName = null) : base(options)
        {
            _casbinModelConfig = casbinModelConfig;
            _schemaName = schemaName;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (string.IsNullOrWhiteSpace(_schemaName) is false)
            {
                modelBuilder.HasDefaultSchema(_schemaName);
            }

            if (_casbinModelConfig is not null)
            {
                modelBuilder.ApplyConfiguration(_casbinModelConfig);
            }
        }

    }
}
