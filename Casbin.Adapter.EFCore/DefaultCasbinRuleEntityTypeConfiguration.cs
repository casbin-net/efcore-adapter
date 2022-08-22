using Casbin.Adapter.EFCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;

namespace Casbin.Adapter.EFCore
{
    public class DefaultCasbinRuleEntityTypeConfiguration<TKey> : IEntityTypeConfiguration<CasbinRule<TKey>> 
        where TKey : IEquatable<TKey>
    {
        private readonly string _tableName;

        public DefaultCasbinRuleEntityTypeConfiguration(string tableName)
        {
            _tableName = tableName;
        }

        public virtual void Configure(EntityTypeBuilder<CasbinRule<TKey>> builder)
        {
            builder.ToTable(_tableName);

            builder.Property(rule => rule.Id).HasColumnName("id");
            builder.Property(rule => rule.PType).HasColumnName("ptype");
            builder.Property(rule => rule.V0).HasColumnName("v0");
            builder.Property(rule => rule.V1).HasColumnName("v1");
            builder.Property(rule => rule.V2).HasColumnName("v2");
            builder.Property(rule => rule.V3).HasColumnName("v3");
            builder.Property(rule => rule.V4).HasColumnName("v4");
            builder.Property(rule => rule.V5).HasColumnName("v5");

            builder.HasIndex(rule => rule.PType);
            builder.HasIndex(rule => rule.V0);
            builder.HasIndex(rule => rule.V1);
            builder.HasIndex(rule => rule.V2);
            builder.HasIndex(rule => rule.V3);
            builder.HasIndex(rule => rule.V4);
            builder.HasIndex(rule => rule.V5);
        }
    }
}
