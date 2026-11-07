using System.Reflection;
using System.Text.Json.Serialization;
using Bogdabot.Server.Shared.Bootstrap;
using Bogdabot.Server.Web.Api.V1.Transformers;
using Bogdabot.Server.Web.Gateway;
using Bogdabot.Server.Web.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

namespace Bogdabot.Server.Web.Bootstrap;

class WebServiceLoader(WebApplicationBuilder builder) : IServiceLoader
{
    private WebApplicationBuilder Builder { get; } = builder;

    public void AddServices()
    {
        Builder.Services.AddControllers();

        Builder.Services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(
                new JsonStringEnumConverter()
            );
            options.JsonSerializerOptions.Converters.Add(
                new SingleValueObjectJsonConverterFactory()
            );
        });

        Builder.Services.AddSingleton<WebTransformer>();
        Builder.Services.AddSingleton<IWebTraderGateway, WebTraderGateway>();

        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        Builder.Services.AddEndpointsApiExplorer();
        Builder.Services.AddSwaggerGen(
            options =>
            {
                options.SwaggerDoc(
                    "v1",
                    new OpenApiInfo
                    {
                        Title = "Bogdabot API",
                        Version = "v1"
                    }
                );

                // Adds support for Swagger examples using <example></example> on the property.
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(
                    AppContext.BaseDirectory,
                    xmlFile);

                options.IncludeXmlComments(xmlPath);
            }
        );

        Builder.Services.PostConfigure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                    );

                var errorResponse = new
                {
                    TraceId = context.HttpContext.TraceIdentifier,
                    Code = 400,
                    Description = "One or more validation errors occurred",
                    Details = errors
                };

                return new BadRequestObjectResult(errorResponse);
            };

            options.SuppressModelStateInvalidFilter = false;
        });
    }
}