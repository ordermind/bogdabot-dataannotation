using FluentMigrator;

namespace Bogdabot.Server.Infrastructure.Postgres.Migrations;

[FluentMigrator.Tags("main")]
[Migration(202607040002)]
public class CreateTradersTable : AutoReversingMigration
{
    public override void Up()
    {
        Create.Table("traders")
            .WithColumn("id").AsInt64().NotNullable().PrimaryKey().Identity()
            .WithColumn("guid").AsGuid().NotNullable().Unique()
            .WithColumn("user_id").AsInt64().NotNullable().Indexed()
            .WithColumn("user_guid").AsGuid().NotNullable().Indexed()
            // Default value not possible because postgres erroneously adds an apostrophe ' after the default value.
            .WithColumn("status").AsString(100).NotNullable().Indexed()
            .WithColumn("broker_name").AsString(100).NotNullable()
            .WithColumn("strategy_name").AsString(100).NotNullable()
            .WithColumn("base_currency").AsString(100).NotNullable()
            .WithColumn("initial_price").AsDecimal(29, 18).NotNullable()
            .WithColumn("initial_base_balance").AsDecimal(29, 18).NotNullable()
            .WithColumn("quote_currency").AsString(100).NotNullable()
            .WithColumn("initial_quote_balance").AsDecimal(29, 18).NotNullable()
            .WithColumn("initial_total_value").AsDecimal(29, 18).NotNullable()
            .WithColumn("first_started_at").AsDateTimeOffset().Nullable()
            .WithColumn("last_started_at").AsDateTimeOffset().Nullable()
            ;

        Create.ForeignKey("fk_traders_users")
            .FromTable("traders").ForeignColumn("user_id")
            .ToTable("users").PrimaryColumn("id");
    }
}
