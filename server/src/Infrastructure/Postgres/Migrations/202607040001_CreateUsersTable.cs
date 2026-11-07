
using FluentMigrator;

namespace Bogdabot.Server.Infrastructure.Postgres.Migrations;

[FluentMigrator.Tags("main")]
[Migration(202607040001)]
public class CreateUsersTable : AutoReversingMigration
{
    public override void Up()
    {
        Create.Table("users")
            .WithColumn("id").AsInt64().NotNullable().PrimaryKey().Identity()
            .WithColumn("guid").AsGuid().NotNullable().Unique()
            // username and normalized username are required by ASP.NET Identity. I deliberately do not use any index here because they will not be used in practice.
            .WithColumn("username").AsString(255).NotNullable()
            .WithColumn("normalized_username").AsString(255).NotNullable()
            // Email will be used for authentication.
            .WithColumn("email").AsString(255).NotNullable()
            // uppercased email so that email addresses are truly unique regardless of uppercase/lowercase combinations in the original address
            .WithColumn("normalized_email").AsString(255).NotNullable().Unique()
            .WithColumn("password_hash").AsString(255).NotNullable()
            .WithColumn("password_changed_at").AsDateTimeOffset().Nullable()
            .WithColumn("email_confirmed")
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(false)
            // Default value not possible because postgres erroneously adds an apostrophe ' after the default value.
            .WithColumn("status")
                .AsString(100)
                .NotNullable()
            .WithColumn("last_login_at").AsDateTimeOffset().Nullable()
            .WithColumn("failed_login_count")
                .AsInt16()
                .NotNullable()
                .WithDefaultValue(0)
            .WithColumn("locked_until").AsDateTimeOffset().Nullable()
            .WithColumn("two_factor_enabled")
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(false)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            ;
    }
}