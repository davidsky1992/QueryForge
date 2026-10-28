using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryForge.Domain.ApiDefinitions;

namespace QueryForge.Infrastructure.Persistence.EntityConfigurations;

internal sealed class ApiDefinitionConfiguration : IEntityTypeConfiguration<ApiDefinition>
{
    public void Configure(EntityTypeBuilder<ApiDefinition> builder)
    {
        builder.ToTable("ApiDefinitions");

        builder.HasKey(apiDefinition => apiDefinition.Id);

        builder.Property(apiDefinition => apiDefinition.Name)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(apiDefinition => apiDefinition.Slug)
            .HasMaxLength(180)
            .IsRequired();

        builder.HasIndex(apiDefinition => apiDefinition.Slug)
            .IsUnique();

        builder.HasIndex(apiDefinition => apiDefinition.Name);
        builder.HasIndex(apiDefinition => apiDefinition.Status);

        builder.Property(apiDefinition => apiDefinition.Description)
            .HasMaxLength(1000);

        builder.Property(apiDefinition => apiDefinition.BaseUrl)
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(apiDefinition => apiDefinition.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(apiDefinition => apiDefinition.AuthenticationType)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(apiDefinition => apiDefinition.DocumentationUrl)
            .HasMaxLength(2048);

        builder.Property(apiDefinition => apiDefinition.Tags)
            .HasMaxLength(500);

        builder.Property(apiDefinition => apiDefinition.RowVersion)
            .IsRowVersion();

        // A secret name only; the value is never stored in the database.
        builder.Property(apiDefinition => apiDefinition.UpstreamSecretName)
            .HasMaxLength(64);

        builder.Property(apiDefinition => apiDefinition.ApiKeyHeaderName)
            .HasMaxLength(64);
    }
}
