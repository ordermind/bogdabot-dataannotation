using Bogdabot.Server.Shared.Actors;
using Bogdabot.Server.Shared.Bootstrap;

namespace Bogdabot.Server.Infrastructure.Akka.Bootstrap;

class AkkaServiceLoader(WebApplicationBuilder builder) : IServiceLoader
{
    private WebApplicationBuilder Builder { get; } = builder;
    public void AddServices()
    {
        Builder.Services.AddSingleton<ActorHostService>();
        Builder.Services.AddSingleton<IAkkaHost>(serviceProvider =>
            serviceProvider.GetRequiredService<ActorHostService>());
        Builder.Services.AddSingleton<IHostedService>(serviceProvider =>
            serviceProvider.GetRequiredService<ActorHostService>());
    }
}