using Microsoft.EntityFrameworkCore;

namespace Casbin.NET.Adapter.EFCore
{
    public partial class CasbinDbContext : DbContext
    {
        public virtual DbSet<CasbinRule> CasbinRule { get; set; }

        private readonly IEntityTypeConfiguration<CasbinRule> _casbinModelConfig;

        public CasbinDbContext()
        {
        }

        public CasbinDbContext(DbContextOptions<CasbinDbContext> options) : base(options)
        {
        }

        public CasbinDbContext(DbContextOptions<CasbinDbContext> options, IEntityTypeConfiguration<CasbinRule> casbinModelConfig) : base(options)
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

    public partial class CasbinRule
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
