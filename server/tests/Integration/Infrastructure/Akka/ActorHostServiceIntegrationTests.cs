using Bogdabot.Server.Account.Entities;
using Bogdabot.Server.Infrastructure.Akka;
using Bogdabot.Server.Shared.Config.Database;
using Bogdabot.Server.Shared.Database;
using Bogdabot.Server.Trading.Actors.Helpers;
using Bogdabot.Server.Trading.Factories;
using Bogdabot.Server.Trading.Repositories;
using Bogdabot.Server.Trading.StateManagement;
using Bogdabot.Server.Trading.Transformers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Bogdabot.Server.Tests.Integration.Infrastructure.Akka;

public class ActorHostServiceIntegrationTests
{
    [Fact]
    public async Task StopAsync_TerminatesActorsAndAllowsActorSystemToRestart()
    {
        var userRepository = Substitute.For<IUserRepository>();
        userRepository.GetAllUsersWithActiveTraders()
            .Returns(Task.FromResult<IEnumerable<UserEntity>>([]));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ITraderLifecycleService>());
        services.AddSingleton(Substitute.For<ITraderDatabaseConfigFactory>());
        services.AddSingleton(Substitute.For<ITraderDbMigrationService>());
        services.AddSingleton(Substitute.For<IPositionRepositoryFactory>());
        services.AddSingleton(userRepository);
        services.AddSingleton<IActorTaskStoreFactory, ActorTaskStoreFactory>();
        services.AddSingleton<TraderActorFactory>();
        services.AddSingleton<TraderWorkflowActorFactory>();
        services.AddSingleton<TraderEntityToTraderConfigurationTransformer>();
        using var serviceProvider = services.BuildServiceProvider();
        var host = new ActorHostService(
            serviceProvider,
            Substitute.For<IHostApplicationLifetime>(),
            Substitute.For<IConfiguration>());

        try
        {
            await host.StartAsync(TestContext.Current.CancellationToken);
            var firstRoot = await host.GetTradingRootSupervisor();

            await host.StopAsync(TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() => host.GetTradingRootSupervisor());

            await host.StartAsync(TestContext.Current.CancellationToken);
            var restartedRoot = await host.GetTradingRootSupervisor();

            Assert.NotSame(firstRoot, restartedRoot);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }
}
