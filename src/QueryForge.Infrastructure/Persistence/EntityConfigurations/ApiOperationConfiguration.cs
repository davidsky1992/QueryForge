using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QueryForge.Domain.ApiDefinitions;
using QueryForge.Domain.ApiOperations;

namespace QueryForge.Infrastructure.Persistence.EntityConfigurations;

internal sealed class ApiOperationConfiguration : IEntityTypeConfiguration<ApiOperation>
{
    public void Configure(EntityTypeBuilder<ApiOperation> builder)
    {
        builder.ToTable("ApiOperations");

        builder.HasKey(operation => operation.Id);

        // Operations belong to their definition: deleting the definition deletes its operations.
        builder.HasOne<ApiDefinition>()
            .WithMany()
            .HasForeignKey(operation => operation.ApiDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(operation => operation.Key)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(operation => new { operation.ApiDefinitionId, operation.Key })
            .IsUnique();

        builder.Property(operation => operation.Method)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(operation => operation.PathTemplate)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(operation => operation.RowVersion)
            .IsRowVersion();

        builder.OwnsMany(operation => operation.Parameters, parameters =>
        {
            parameters.ToTable("ApiOperationParameters");
            parameters.WithOwner().HasForeignKey("ApiOperationId");
            parameters.Property<int>("Id");
            parameters.HasKey("Id");
            parameters.HasIndex("ApiOperationId", nameof(OperationParameter.Name)).IsUnique();

            parameters.Property(parameter => parameter.Name).HasMaxLength(64).IsRequired();
            parameters.Property(parameter => parameter.Location).HasConversion<string>().HasMaxLength(16).IsRequired();
            parameters.Property(parameter => parameter.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
            parameters.Property(parameter => parameter.Minimum).HasPrecision(38, 10);
            parameters.Property(parameter => parameter.Maximum).HasPrecision(38, 10);
            parameters.Property(parameter => parameter.Pattern).HasMaxLength(256);
            parameters.Property(parameter => parameter.DefaultValue).HasMaxLength(1024);

            // A small list (at most 50 values) stored as a JSON array.
            parameters.Property(parameter => parameter.AllowedValues)
                .HasConversion(
                    values => JsonSerializer.Serialize(values, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>(),
                    new ValueComparer<IReadOnlyList<string>>(
                        (left, right) => left!.SequenceEqual(right!),
                        values => values.Aggregate(0, (hash, value) => HashCode.Combine(hash, value.GetHashCode())),
                        values => values.ToList()))
                .IsRequired();
        });

        // Parameters are always loaded with their operation and written through the private list.
        builder.Navigation(operation => operation.Parameters)
            .HasField("_parameters")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
