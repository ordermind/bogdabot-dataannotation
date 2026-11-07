using System.ComponentModel.DataAnnotations;
using Bogdabot.Server.Web.Api.V1.Controllers;

namespace Bogdabot.Server.Tests.Unit.Web.Api.V1.Controllers;

public class CreateTraderFormDataValidationTests
{
    [Fact]
    public void Validation_Succeeds_ForValidFormData()
    {
        var model = new CreateTraderFormData
        {
            BrokerName = "IBKR",
            StrategyName = "SpotGrid",
            Base = "BTC",
            Quote = "EUR",
            InitialBaseInvestment = 50m,
            InitialQuoteInvestment = 25m
        };

        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);

        var isValid = Validator.TryValidateObject(model, context, results, validateAllProperties: true);

        Assert.True(isValid);
        Assert.Empty(results);
    }

    [Fact]
    public void Validation_Fails_ForInvalidFormData()
    {
        var model = new CreateTraderFormData
        {
            BrokerName = "InvalidExchange",
            StrategyName = "Nope",
            Base = "",
            Quote = null,
            InitialBaseInvestment = 0m,
            InitialQuoteInvestment = -1m
        };

        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);

        var isValid = Validator.TryValidateObject(model, context, results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.NotEmpty(results);
    }
}
