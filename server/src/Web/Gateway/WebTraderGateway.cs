using Akka.Actor;
using Bogdabot.Server.Shared.Actors;
using Bogdabot.Server.Shared.Trading;
using Bogdabot.Server.Shared.Trading.Config;
using Bogdabot.Server.Shared.ValueObjects;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.DeleteTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StartTraderProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopAllTradersForUserProtocol;
using static Bogdabot.Server.Shared.Trading.MessageProtocols.TraderProtocols.StopTraderProtocol;

namespace Bogdabot.Server.Web.Gateway;

public class WebTraderGateway(IAkkaHost akkaHost) : IWebTraderGateway
{
    private IAkkaHost AkkaHost { get; } = akkaHost;

    public async Task<Result<NonEmptyGuid>> CreateTrader(TraderConfiguration config)
    {
        var traderSupervisor = await GetPerUserTraderSupervisor(config.UserGuid);

        var requestMessage = new StartNewTraderRequest
        {
            Config = config
        };

        var response = await traderSupervisor.Ask(requestMessage);

        return response switch
        {
            ExistingTraderStartedResponse successResponse =>
                new Result<NonEmptyGuid>.Ok(successResponse.TraderIdentifier.TraderGuid),

            NewTraderNotStartedResponse errorResponse =>
                new Result<NonEmptyGuid>.Error(errorResponse.Error),

            _ => throw new InvalidOperationException(
                $"Unexpected response type: {response.GetType().Name}"),
        };
    }

    public async Task<Result<NonEmptyGuid>> StartActiveTrader(TraderIdentifier traderIdentifier)
    {
        var request = new StartActiveTraderRequest
        {
            TraderIdentifier = traderIdentifier
        };

        var traderSupervisor = await GetPerUserTraderSupervisor(traderIdentifier.UserGuid);

        var response = await traderSupervisor.Ask(request);

        return response switch
        {
            ExistingTraderStartedResponse successResponse =>
                new Result<NonEmptyGuid>.Ok(successResponse.TraderIdentifier.TraderGuid),

            ExistingTraderNotStartedResponse errorResponse =>
                new Result<NonEmptyGuid>.Error(errorResponse.Error),

            _ => throw new InvalidOperationException(
                $"Unexpected response type: {response.GetType().Name}"),
        };
    }

    public async Task<Result<NonEmptyGuid>> StopOneTrader(TraderIdentifier traderIdentifier)
    {
        var traderSupervisor = await GetPerUserTraderSupervisor(traderIdentifier.UserGuid);

        var requestMessage = new StopTraderRequest
        {
            TraderIdentifier = traderIdentifier
        };

        var response = await traderSupervisor.Ask(requestMessage);

        return response switch
        {
            TraderStoppedResponse successResponse =>
                new Result<NonEmptyGuid>.Ok(successResponse.TraderIdentifier.TraderGuid),

            TraderNotStoppedResponse errorResponse =>
                new Result<NonEmptyGuid>.Error(errorResponse.Error),

            _ => throw new InvalidOperationException(
                $"Unexpected response type: {response.GetType().Name}"),
        };
    }

    public async Task<Result<NonEmptyGuid>> StopAllTradersForUser(NonEmptyGuid userGuid)
    {
        var requestMessage = new StopAllTradersRequest
        {
            UserGuid = userGuid
        };

        var tradingRootSupervisor = await AkkaHost.GetTradingRootSupervisor();

        var response = await tradingRootSupervisor.Ask(requestMessage);

        return response switch
        {
            AllTradersStoppedResponse successResponse =>
                new Result<NonEmptyGuid>.Ok(successResponse.UserGuid),

            AllTradersNotStoppedResponse errorResponse =>
                new Result<NonEmptyGuid>.Error(errorResponse.Error),

            _ => throw new InvalidOperationException(
                $"Unexpected response type: {response.GetType().Name}"),
        };
    }

    public async Task<Result<NonEmptyGuid>> DeleteTrader(TraderIdentifier traderIdentifier)
    {
        var traderSupervisor = await GetPerUserTraderSupervisor(traderIdentifier.UserGuid);

        var requestMessage = new DeleteTraderRequest
        {
            TraderIdentifier = traderIdentifier
        };

        var response = await traderSupervisor.Ask(requestMessage);

        return response switch
        {
            TraderDeletedResponse successResponse =>
                new Result<NonEmptyGuid>.Ok(successResponse.TraderIdentifier.TraderGuid),

            TraderNotDeletedResponse errorResponse =>
                new Result<NonEmptyGuid>.Error(errorResponse.Error),

            _ => throw new InvalidOperationException(
                $"Unexpected response type: {response.GetType().Name}"),
        };
    }

    private async Task<IActorRef> GetPerUserTraderSupervisor(NonEmptyGuid userGuid)
    {
        var traderSupervisor = await this.AkkaHost.GetTraderSupervisorForUser(userGuid);

        if (traderSupervisor.IsNobody())
        {
            throw new ActorNotFoundException($"Error creating trader: The trader supervisor actor for user {userGuid} could not be found!");
        }

        return traderSupervisor;
    }
}