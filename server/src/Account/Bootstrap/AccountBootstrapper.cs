using Bogdabot.Server.Shared.Bootstrap;
using Bogdabot.Server.Shared.Users;

namespace Bogdabot.Server.Account.Bootstrap;

public class AccountBootstrapper(WebApplication app) : IBootstrapper
{
    private WebApplication App { get; } = app;

    public async Task Run()
    {
        var accountManager = App.Services.GetRequiredService<IGeneratesSuperadminUsers>();
        await accountManager.CreateSuperadminUser();
    }
}