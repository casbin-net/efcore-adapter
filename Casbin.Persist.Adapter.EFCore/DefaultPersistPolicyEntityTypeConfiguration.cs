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
            builder.Ignore(p => p.Value7);
            builder.Ignore(p => p.Value8);
            builder.Ignore(p => p.Value9);
            builder.Ignore(p => p.Value10);
            builder.Ignore(p => p.Value11);
            builder.Ignore(p => p.Value12);
            builder.Ignore(p => p.Value13);
            builder.Ignore(p => p.Value14);

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