using Bogdabot.Server.Shared.Bootstrap;

namespace Bogdabot.Server.Web.Bootstrap;

public class WebBootstrapper(WebApplication app) : IBootstrapper
{
    private WebApplication App { get; } = app;
    public async Task Run()
    {
        // Configure the HTTP request pipeline.
        if (App.Environment.IsDevelopment())
        {
            App.UseSwagger();
            App.UseSwaggerUI();
        }

        App.UseAuthorization();

        App.MapControllers();
    }
}