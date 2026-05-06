using Microsoft.Extensions.Options;
using PoMemeVideo.Api.Configuration;

namespace PoMemeVideo.Api.Features.Config;

public static class ConfigEndpoints
{
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/config", (
            IOptions<FeatureFlags> featureFlags,
            IHostEnvironment environment) =>
        {
            return Results.Ok(new
            {
                useMockAI = featureFlags.Value.UseMockAI,
                isDevelopment = environment.IsDevelopment()
            });
        })
        .WithName("GetConfig")
        .WithTags("Config")
        .Produces<object>(200)
        .AllowAnonymous();

        return app;
    }
}
