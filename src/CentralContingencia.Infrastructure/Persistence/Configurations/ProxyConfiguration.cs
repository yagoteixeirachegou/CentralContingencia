using CentralContingencia.Domain.Proxies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CentralContingencia.Infrastructure.Persistence.Configurations;

public sealed class ProxyConfiguration : IEntityTypeConfiguration<Proxy>
{
    public void Configure(EntityTypeBuilder<Proxy> entity)
    {
        entity.ToTable("Proxies", "proxies", table =>
        {
            table.HasCheckConstraint("CK_Proxies_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_Proxies_Port", "\"Port\" BETWEEN 1 AND 65535");
            table.HasCheckConstraint("CK_Proxies_Host", "length(btrim(\"Host\")) BETWEEN 1 AND 253 AND \"Host\" = lower(\"Host\") AND \"Host\" !~ '[[:space:]/@?#]'");
            table.HasCheckConstraint("CK_Proxies_Username", "length(btrim(\"Username\")) BETWEEN 1 AND 128 AND \"Username\" !~ '[[:cntrl:]]'");
            table.HasCheckConstraint("CK_Proxies_Region", "length(btrim(\"Region\")) BETWEEN 1 AND 100 AND \"Region\" !~ '[[:cntrl:]]'");
            table.HasCheckConstraint("CK_Proxies_Status", "\"Status\" IN ('Active', 'Suspended', 'Disabled')");
            table.HasCheckConstraint("CK_Proxies_Timestamps", "isfinite(\"CreatedAt\") AND isfinite(\"UpdatedAt\") AND isfinite(\"ExpiresAt\") AND \"UpdatedAt\" >= \"CreatedAt\"");
            table.HasCheckConstraint("CK_Proxies_Password", "octet_length(\"PasswordNonce\") = 12 AND octet_length(\"PasswordTag\") = 16 AND octet_length(\"PasswordCiphertext\") BETWEEN 1 AND 1024 AND \"PasswordKeyId\" ~ '^[A-Za-z0-9_.-]{1,64}$'");
        });
        entity.HasKey(p => p.Id);
        entity.Property<uint>("xmin").IsRowVersion();
        entity.Property(p => p.Id).ValueGeneratedNever();
        entity.Property(p => p.Host).HasConversion(h => h.Value, value => ProxyHost.Create(value)).HasMaxLength(253).IsRequired();
        entity.Property(p => p.Port).HasConversion(p => p.Value, value => ProxyPort.Create(value)).IsRequired();
        entity.Property(p => p.Username).HasMaxLength(128).IsRequired();
        entity.Property(p => p.Region).HasMaxLength(100).IsRequired();
        entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.HasIndex(p => new { p.Host, p.Port });
        entity.HasIndex(p => new { p.Status, p.ExpiresAt });
        entity.HasIndex(p => p.Region);
        entity.OwnsOne(p => p.Password, password =>
        {
            password.Property(p => p.KeyId).HasColumnName("PasswordKeyId").HasMaxLength(64).IsRequired();
            password.Property(p => p.Nonce).HasField("nonce").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnName("PasswordNonce").IsRequired();
            password.Property(p => p.Ciphertext).HasField("ciphertext").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnName("PasswordCiphertext").IsRequired();
            password.Property(p => p.Tag).HasField("tag").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnName("PasswordTag").IsRequired();
            password.HasIndex(p => new { p.KeyId, p.Nonce }).IsUnique().HasDatabaseName("UX_Proxies_PasswordKeyId_Nonce");
        });
        entity.Navigation(p => p.Password).IsRequired();
    }
}
