using Bogdabot.Server.Infrastructure.Miscellaneous.Database;
using Bogdabot.Server.Infrastructure.Miscellaneous.Database.Extensions;
using Bogdabot.Server.Shared.Bootstrap;

namespace Bogdabot.Server.Infrastructure.Miscellaneous.Bootstrap;

class MiscellaneousServiceLoader(WebApplicationBuilder builder) : IServiceLoader
{
    private WebApplicationBuilder Builder { get; } = builder;

    public void AddServices()
    {
        Builder.Services.AddSingleton<IDbOptionsFactory, DatabaseContextFactory>();
    }
}