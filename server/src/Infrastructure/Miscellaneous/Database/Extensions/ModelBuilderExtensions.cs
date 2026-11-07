using Bogdabot.Server.Infrastructure.Miscellaneous.Database.Configurations;
using Bogdabot.Server.Shared.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bogdabot.Server.Infrastructure.Miscellaneous.Database.Extensions;

public static class ModelBuilderExtensions
{
    public static void ConfigureSingleValueObjects(
        this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.ClrType.GetProperties())
            {
                var propertyType = property.PropertyType;

                var interfaceType = propertyType
                    .GetInterfaces()
                    .FirstOrDefault(i =>
                        i.IsGenericType &&
                        i.GetGenericTypeDefinition() == typeof(ISingleValueObject<,>) &&
                        i.GetGenericArguments()[1] == propertyType);

                if (interfaceType is null)
                    continue;

                var primitiveType = interfaceType.GetGenericArguments()[0];

                var converterType = typeof(SingleValueObjectConverter<,>)
                    .MakeGenericType(propertyType, primitiveType);

                var converter = Activator.CreateInstance(converterType);

                modelBuilder.Entity(entityType.ClrType)
                    .Property(property.Name)
                    .HasConversion((ValueConverter)converter!);
            }
        }
    }

    public static void ConfigureEnumConversion(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType.IsEnum)
                {
                    var converterType = typeof(EnumToStringConverter<>)
                        .MakeGenericType(property.ClrType);

                    var converter = Activator.CreateInstance(converterType);

                    property.SetValueConverter(
                        (ValueConverter)converter!);
                }
            }
        }
    }
}