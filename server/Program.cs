using Bogdabot.Server.Account.Bootstrap;
using Bogdabot.Server.Infrastructure;
using Bogdabot.Server.Shared.Bootstrap;
using Bogdabot.Server.Trading.Bootstrap;
using Bogdabot.Server.Web.Bootstrap;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Bogdabot.Server;

class Program
{
    private static void AddServiceLoaders(WebApplicationBuilder builder)
    {
        var sharedServiceLoader = new SharedServiceLoader(builder);
        sharedServiceLoader.AddServices();

        var infrastructureServiceLoader = new InfrastructureServiceLoader(builder, sharedServiceLoader.EnvironmentVariableService!);
        infrastructureServiceLoader.AddServices();

        var accountServiceLoader = new AccountServiceLoader(builder);
        accountServiceLoader.AddServices();

        var tradingServiceLoader = new TradingServiceLoader(builder);
        tradingServiceLoader.AddServices();

        var webServiceLoader = new WebServiceLoader(builder);
        webServiceLoader.AddServices();
    }

    private static async Task RunBootstrappers(WebApplication app)
    {
        var sharedBootstrapper = new SharedBootstrapper();
        await sharedBootstrapper.Run();

        var infrastructureBootstrapper = new InfrastructureBootstrapper(app);
        await infrastructureBootstrapper.Run();

        var accountBootstrapper = new AccountBootstrapper(app);
        await accountBootstrapper.Run();

        var tradingBootstrapper = new TradingBootstrapper();
        await tradingBootstrapper.Run();

        var webBootstrapper = new WebBootstrapper(app);
        await webBootstrapper.Run();
    }

    static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        AddServiceLoaders(builder);

        // Make sure certain controllers are only available in Development mode.
        Type[] devOnlyControllers = GetDevOnlyControllers();
        builder.Services.AddControllers()
            .ConfigureApplicationPartManager(apm =>
            {
                if (!builder.Environment.IsDevelopment())
                {
                    apm.FeatureProviders.Add(
                        new ExcludeControllerFeatureProvider(devOnlyControllers)
                    );
                }
            });

        var app = builder.Build();

        await RunBootstrappers(app);

        await app.RunAsync();
    }

    static Type[] GetDevOnlyControllers()
    {
        var controllers = new[]
        {
            typeof(Bogdabot.Server.Web.Api.V1.Controllers.Debug.DebugWebController),
            typeof(
                Bogdabot.Server.Web.Api.V1.Controllers.Debug.DebugTraderController
            ),
        };

        foreach (var controller in controllers)
        {
            if (!typeof(ControllerBase).IsAssignableFrom(controller) ||
                controller.GetCustomAttributes(typeof(ApiControllerAttribute), inherit: true).Length == 0)
            {
                throw new ArgumentException(
                    $"{controller.FullName} is not a valid controller type.",
                    nameof(controllers));
            }
        }

        return controllers;
    }
}

internal class ExcludeControllerFeatureProvider(IEnumerable<Type> controllerTypes) : IApplicationFeatureProvider<ControllerFeature>
{
    private readonly HashSet<Type> _controllerTypes = [.. controllerTypes];

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        for (var i = feature.Controllers.Count - 1; i >= 0; i--)
        {
            if (_controllerTypes.Contains(feature.Controllers[i].AsType()))
            {
                feature.Controllers.RemoveAt(i);
            }
        }
    }
}
