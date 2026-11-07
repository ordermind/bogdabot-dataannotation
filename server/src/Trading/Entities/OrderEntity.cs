using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Enums;
using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Trading.Entities
{
    [Index(nameof(ExternalId), IsUnique = true)]
    public class OrderEntity()
    {
        public ulong? Id { get; set; }

        public required string ExternalId { get; set; }

        /// <summary>
        /// The security to buy or sell, also known as base currency.
        /// </summary>
        public required string Base { get; set; }

        /// <summary>
        /// The currency for the payment, also known as quote currency.
        /// </summary>
        public required string Quote { get; set; }

        /// <summary>
        /// Determines whether it's a buy or a sell order.
        /// </summary>
        public required OrderActionEnum Action { get; set; }

        /// <summary>
        /// The type of order, such as market order or limit order.
        /// </summary>
        public required OrderTypeEnum Type { get; set; }

        public required OrderStatusEnum Status { get; set; }

        public required PositiveDecimalNumber DesiredPrice { get; set; }

        public required PositiveDecimalNumber DesiredQuantity { get; set; }

        public PositiveDecimalNumber? ActualPrice { get; set; }

        public PositiveDecimalNumber? ActualQuantity { get; set; }

        public NonNegativeDecimalNumber? TotalFees { get; set; }

        public DateTimeOffset OpenedAt { get; set; }

        public DateTimeOffset? ClosedAt { get; set; }
    }
}