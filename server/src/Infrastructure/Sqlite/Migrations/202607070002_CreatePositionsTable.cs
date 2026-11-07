
using FluentMigrator;

namespace Bogdabot.Server.Infrastructure.Sqlite.Migrations;

[FluentMigrator.Tags("trader")]
[Migration(202607070002)]
public class CreatePositionsTable : AutoReversingMigration
{
    public override void Up()
    {
        Create.Table("positions")
            .WithColumn("id").AsInt64().NotNullable().PrimaryKey().Identity()
            .WithColumn("base").AsString().NotNullable()
            .WithColumn("quote").AsString().NotNullable()
            .WithColumn("quantity").AsString().NotNullable() // Decimal
            .WithColumn("desired_profit_margin").AsString().NotNullable() // Decimal
            .WithColumn("buy_order_external_id").AsString().NotNullable().Unique()
            .WithColumn("bought_at").AsDateTime().NotNullable()
            .WithColumn("buy_price").AsString().NotNullable() // Decimal
            .WithColumn("buy_fee").AsString().NotNullable() // Decimal
            .WithColumn("sell_order_external_id").AsString().Nullable().Unique()
            .WithColumn("sold_at").AsDateTime().Nullable()
            .WithColumn("sell_price").AsString().Nullable() // Decimal
            .WithColumn("sell_fee").AsString().Nullable() // Decimal
            .WithColumn("realized_result").AsString().Nullable() // Decimal
            .WithColumn("actual_profit_margin").AsString().Nullable() // Decimal
            ;
    }
}