
using FluentMigrator;

namespace Bogdabot.Server.Infrastructure.Sqlite.Migrations;

[FluentMigrator.Tags("trader")]
[Migration(202607070001)]
public class CreateOrdersTable : AutoReversingMigration
{
    public override void Up()
    {
        Create.Table("orders")
            .WithColumn("id").AsInt64().NotNullable().PrimaryKey().Identity()
            .WithColumn("external_id").AsString().NotNullable().Unique()
            .WithColumn("base").AsString().NotNullable()
            .WithColumn("quote").AsString().NotNullable()
            .WithColumn("action").AsString().NotNullable().Indexed()
            .WithColumn("type").AsString().NotNullable()
            .WithColumn("status").AsString().NotNullable().Indexed()
            .WithColumn("desired_price").AsString().NotNullable() // Decimal
            .WithColumn("desired_quantity").AsString().NotNullable() // Decimal
            .WithColumn("actual_price").AsString().Nullable() // Decimal
            .WithColumn("actual_quantity").AsString().Nullable() // Decimal
            .WithColumn("total_fees").AsString().Nullable() // Decimal
            .WithColumn("opened_at").AsDateTime().NotNullable()
            .WithColumn("closed_at").AsDateTime().Nullable()
            ;
    }
}