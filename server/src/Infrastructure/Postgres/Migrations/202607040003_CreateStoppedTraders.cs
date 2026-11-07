using FluentMigrator;

namespace Bogdabot.Server.Infrastructure.Postgres.Migrations;

[FluentMigrator.Tags("main")]
[Migration(202607040003)]
public class CreateStoppedTradersTable : AutoReversingMigration
{
    public override void Up()
    {
        Create.Table("tradersstopped")
            .WithColumn("id").AsInt64().NotNullable().PrimaryKey().Identity()
            .WithColumn("guid").AsGuid().NotNullable().Unique()
            .WithColumn("user_id").AsInt64().NotNullable().Indexed()
            .WithColumn("user_guid").AsGuid().NotNullable().Indexed()
            .WithColumn("broker_name").AsString(100).NotNullable()
            .WithColumn("strategy_name").AsString(100).NotNullable()
            .WithColumn("base_currency").AsString(100).NotNullable()
            .WithColumn("initial_price").AsDecimal(29, 18).NotNullable()
            .WithColumn("initial_base_balance").AsDecimal(29, 18).NotNullable()
            .WithColumn("final_price").AsDecimal(29, 18).NotNullable()
            .WithColumn("final_base_balance").AsDecimal(29, 18).NotNullable()
            .WithColumn("quote_currency").AsString(100).NotNullable()
            .WithColumn("initial_quote_balance").AsDecimal(29, 18).NotNullable()
            .WithColumn("final_quote_balance").AsDecimal(29, 18).NotNullable()
            .WithColumn("initial_total_value").AsDecimal(29, 18).NotNullable()
            .WithColumn("final_total_value").AsDecimal(29, 18).NotNullable()
            .WithColumn("unrealized_result").AsDecimal(29, 18).NotNullable()
            .WithColumn("realized_result").AsDecimal(29, 18).NotNullable()
            .WithColumn("total_result").AsDecimal(29, 18).NotNullable()
            .WithColumn("unrealized_profit_margin").AsDecimal(29, 18).NotNullable()
            .WithColumn("realized_profit_margin").AsDecimal(29, 18).NotNullable()
            .WithColumn("total_profit_margin").AsDecimal(29, 18).NotNullable()
            .WithColumn("started_at").AsDateTimeOffset().Nullable()
            .WithColumn("stopped_at").AsDateTimeOffset().Nullable()
            ;

        Create.ForeignKey("fk_tradersstopped_users")
            .FromTable("tradersstopped").ForeignColumn("user_id")
            .ToTable("users").PrimaryColumn("id");
    }
}
