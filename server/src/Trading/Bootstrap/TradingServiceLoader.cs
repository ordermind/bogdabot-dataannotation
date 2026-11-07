using Bogdabot.Server.Shared.Bootstrap;
using Bogdabot.Server.Trading.Factories;
using Bogdabot.Server.Trading.Actors.Helpers;
using Bogdabot.Server.Trading.StateManagement;
using Bogdabot.Server.Trading.Transformers;

namespace Bogdabot.Server.Trading.Bootstrap;

class TradingServiceLoader(WebApplicationBuilder builder) : IServiceLoader
{
    private WebApplicationBuilder Builder { get; } = builder;

    public void AddServices()
    {
        Builder.Services.AddSingleton<ITraderLifecycleService, TraderLifecycleService>();
        Builder.Services.AddSingleton<TraderActorFactory>();
        Builder.Services.AddSingleton<TraderWorkflowActorFactory>();
        Builder.Services.AddSingleton<IActorTaskStoreFactory, ActorTaskStoreFactory>();
        Builder.Services.AddSingleton<TraderEntityToTraderConfigurationTransformer>();
    }
}
