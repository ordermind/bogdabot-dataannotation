using Bogdabot.Server.Shared.Bootstrap;
using Bogdabot.Server.Shared.Users;
using Bogdabot.Server.Account.Managers;

namespace Bogdabot.Server.Account.Bootstrap;

class AccountServiceLoader(WebApplicationBuilder builder) : IServiceLoader
{
    private WebApplicationBuilder Builder { get; } = builder;

    public void AddServices()
    {
        this.Builder.Services.AddSingleton<IGeneratesSuperadminUsers, AccountManager>();
        this.Builder.Services.AddScoped<IFetchesCurrentUser, AccountManager>();
    }
}