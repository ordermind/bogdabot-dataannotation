using Akka.Actor;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Database;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.ValueObjects;
using Bogdabot.Server.Trading.Actors.Helpers;
using Bogdabot.Server.Trading.Actors.WorkflowActors;
using Bogdabot.Server.Trading.StateManagement;
using Bogdabot.Server.Trading.Transformers;

namespace Bogdabot.Server.Trading.Factories;

public class TraderWorkflowActorFactory(
    ITraderDatabaseConfigFactory traderDbConfigFactory,
    ITraderDbMigrationService traderMigrationService,
    ITraderLifecycleService lifecycleService,
    IPositionRepositoryFactory positionRepositoryFactory,
    TraderEntityToTraderConfigurationTransformer traderConfigTransformer,
    ILoggerFactory loggerFactory
)
{
    private ITraderDatabaseConfigFactory TraderDbConfigFactory { get; } = traderDbConfigFactory;
    private ITraderDbMigrationService TraderMigrationService { get; } = traderMigrationService;
    private ITraderLifecycleService LifecycleService { get; } = lifecycleService;
    private IPositionRepositoryFactory PositionRepositoryFactory { get; } = positionRepositoryFactory;
    private TraderEntityToTraderConfigurationTransformer TraderConfigTransformer { get; } = traderConfigTransformer;
    private ILoggerFactory LoggerFactory { get; } = loggerFactory;

    public IActorRef CreateStartExistingTraderWorkFlowActor(
        IActorContext context,
        TraderIdentifier traderIdentifier
    )
    {
        return context.ActorOf(
            Props.Create(() =>
                new StartExistingTraderWorkflowActor(
                    traderDbConfigFactory: TraderDbConfigFactory,
                    traderMigrationService: TraderMigrationService,
                    lifecycleService: LifecycleService,
                    logger: LoggerFactory.CreateLogger<StartExistingTraderWorkflowActor>(),
                    traderIdentifier: traderIdentifier
                )
            ),
            WorkflowActorId.FromGuid(NonEmptyGuid.NewGuid()));
    }

    public IActorRef CreateStopTraderWorkFlowActor(
        IActorContext context,
        TraderIdentifier traderIdentifier,
        IActorRef brokerClientSupervisorReference
    )
    {
        return context.ActorOf(
            Props.Create(() =>
                new StopTraderWorkflowActor(
                    lifecycleService: LifecycleService,
                    traderDbConfigFactory: TraderDbConfigFactory,
                    positionRepositoryFactory: PositionRepositoryFactory,
                    traderConfigTransformer: TraderConfigTransformer,
                    logger: LoggerFactory.CreateLogger<StopTraderWorkflowActor>(),
                    traderIdentifier: traderIdentifier,
                    brokerClientSupervisorReference: brokerClientSupervisorReference
                )
            ),
            WorkflowActorId.FromGuid(NonEmptyGuid.NewGuid()));
    }
    public IActorRef CreateDeleteTraderWorkFlowActor(
        IActorContext context,
        TraderIdentifier traderIdentifier
    )
    {
        return context.ActorOf(
            Props.Create(() =>
                new DeleteTraderWorkflowActor(
                    lifecycleService: LifecycleService,
                    traderDbConfigFactory: TraderDbConfigFactory,
                    logger: LoggerFactory.CreateLogger<DeleteTraderWorkflowActor>(),
                    traderIdentifier: traderIdentifier
                )
            ),
            WorkflowActorId.FromGuid(NonEmptyGuid.NewGuid()));
    }
}