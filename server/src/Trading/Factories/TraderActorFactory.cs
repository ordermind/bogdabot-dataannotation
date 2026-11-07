using Akka.Actor;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Database;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Trading.Actors;
using Bogdabot.Server.Trading.Actors.Helpers;

namespace Bogdabot.Server.Trading.Factories;

public class TraderActorFactory(
    ITraderDatabaseConfigFactory dbConfigFactory,
    ITraderDbMigrationService migrationService,
    ILoggerFactory loggerFactory
)
{
    private ITraderDatabaseConfigFactory DbConfigFactory { get; } = dbConfigFactory;
    private ITraderDbMigrationService MigrationService { get; } = migrationService;
    private ILoggerFactory LoggerFactory { get; } = loggerFactory;

    public IActorRef Create(
        IActorContext context,
        TraderConfiguration config,
        TraderIdentifier traderIdentifier
    )
    {
        return context.ActorOf(
            Props.Create(() =>
                new TraderActor(
                    DbConfigFactory,
                    MigrationService,
                    config,
                    traderIdentifier,
                    LoggerFactory.CreateLogger<TraderActor>()
                )
            ),
            TraderActorId.FromTraderGuid(traderIdentifier.TraderGuid));
    }
}