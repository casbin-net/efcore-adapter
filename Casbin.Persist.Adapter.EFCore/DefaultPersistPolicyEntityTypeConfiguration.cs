using System;
using Casbin.Persist.Adapter.EFCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Casbin.Persist.Adapter.EFCore
{
    public class DefaultPersistPolicyEntityTypeConfiguration<TKey> : IEntityTypeConfiguration<EFCorePersistPolicy<TKey>> 
        where TKey : IEquatable<TKey>
    {
        private readonly string _tableName;

        public DefaultPersistPolicyEntityTypeConfiguration(string tableName)
        {
            _tableName = tableName;
        }

        public virtual void Configure(EntityTypeBuilder<EFCorePersistPolicy<TKey>> builder)
        {
            builder.ToTable(_tableName);

            builder.Property(p => p.Id).HasColumnName("id");
            builder.Ignore(p => p.Section);
            builder.Property(p => p.Type).HasColumnName("ptype");
            builder.Property(p => p.Value1).HasColumnName("v0");
            builder.Property(p => p.Value2).HasColumnName("v1");
            builder.Property(p => p.Value3).HasColumnName("v2");
            builder.Property(p => p.Value4).HasColumnName("v3");
            builder.Property(p => p.Value5).HasColumnName("v4");
            builder.Property(p => p.Value6).HasColumnName("v5");
            builder.Property(p => p.Value7).HasColumnName("v6");
            builder.Property(p => p.Value8).HasColumnName("v7");
            builder.Property(p => p.Value9).HasColumnName("v8");
            builder.Property(p => p.Value10).HasColumnName("v9");
            builder.Property(p => p.Value11).HasColumnName("v10");
            builder.Property(p => p.Value12).HasColumnName("v11");
            builder.Property(p => p.Value13).HasColumnName("v12");
            builder.Property(p => p.Value14).HasColumnName("v13");

            builder.HasIndex(p => p.Type);
            builder.HasIndex(p => p.Value1);
            builder.HasIndex(p => p.Value2);
            builder.HasIndex(p => p.Value3);
            builder.HasIndex(p => p.Value4);
            builder.HasIndex(p => p.Value5);
            builder.HasIndex(p => p.Value6);
        }
    }
}