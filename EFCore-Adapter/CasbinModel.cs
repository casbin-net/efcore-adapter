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

    public partial class CasbinRule<TKey> : ICasbinRule where TKey : IEquatable<TKey>
    {
        public virtual TKey Id { get; set; }
        public string PType { get; set; }
        public string V0 { get; set; }
        public string V1 { get; set; }
        public string V2 { get; set; }
        public string V3 { get; set; }
        public string V4 { get; set; }
        public string V5 { get; set; }

    }

    public interface ICasbinRule
    {
        string PType { get; set; }
        string V0 { get; set; }
        string V1 { get; set; }
        string V2 { get; set; }
        string V3 { get; set; }
        string V4 { get; set; }
        string V5 { get; set; }
    }
}
