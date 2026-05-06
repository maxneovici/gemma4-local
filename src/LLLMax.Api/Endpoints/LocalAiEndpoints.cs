using LLLMax.Api.Models;
using LLLMax.Api.Options;
using LLLMax.Api.Services;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Endpoints;

public static class LocalAiEndpoints
{
    public static IEndpointRouteBuilder MapLocalAiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", (IOptions<LocalAiOptions> options) => Results.Ok(new
        {
            Service = "LLLMax local AI assistant",
            options.Value.AppName,
            options.Value.Provider,
            options.Value.DefaultModel,
            options.Value.BaseUrl,
            options.Value.ManageProcess,
            OpenAiCompatibleBaseUrl = new Uri(new Uri(options.Value.BaseUrl), "/v1/").ToString(),
            Frontend = "/",
            ApiExplorer = "/swagger",
            Endpoints = new[] { "GET /health", "GET /models", "POST /sessions/{id}/chat", "POST /documents/upload", "POST /documents/ocr", "POST /documents/extract-invoice", "POST /integrations/apis/discover", "POST /chat" }
        }));

        app.MapGet("/health", async (IOllamaApi ollamaApi, CancellationToken cancellationToken) =>
        {
            var isHealthy = await ollamaApi.IsHealthyAsync(cancellationToken);

            return isHealthy
                ? Results.Ok(new HealthResponse(true, "Ollama is reachable"))
                : Results.Problem("Ollama is not reachable.");
        });

        app.MapGet("/models", async (ILocalChatClient chatClient, CancellationToken cancellationToken) =>
            Results.Ok(await chatClient.GetModelsAsync(cancellationToken)));

        app.MapGet("/models/openai", async (IOllamaApi ollamaApi, CancellationToken cancellationToken) =>
            Results.Ok(await ollamaApi.GetOpenAiModelsAsync(cancellationToken)));

        app.MapGet("/models/available", async (
            IOptions<LocalAiOptions> options,
            IOllamaApi ollamaApi,
            CancellationToken cancellationToken) =>
        {
            var installed = await ollamaApi.GetModelsAsync(cancellationToken);
            var installedNames = installed.Select(model => model.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var configuredModels = options.Value.RequiredModels.Count > 0
                ? options.Value.RequiredModels
                :
                [
                    new LocalModelSeed(options.Value.DefaultModel, "chat", true, "Default chat model."),
                    new LocalModelSeed(options.Value.Memory.EmbeddingModel, "embedding", options.Value.Memory.Enabled, "Default embedding model.")
                ];

            return Results.Ok(configuredModels.Select(model => new AvailableModelResponse(
                Name: model.Name,
                Kind: model.Kind,
                Installed: installedNames.Contains(model.Name) || installedNames.Contains($"{model.Name}:latest"),
                PullOnStartup: model.PullOnStartup,
                Description: model.Description)));
        });

        app.MapGet("/setup", (ILocalModelSetupService setupService) => Results.Ok(setupService.GetSnapshot()));

        app.MapPost("/setup/run", async (ILocalModelSetupService setupService, CancellationToken cancellationToken) =>
        {
            await setupService.EnsureStartupModelsAsync(cancellationToken);
            return Results.Ok(setupService.GetSnapshot());
        });

        app.MapPost("/setup/pull", async (PullModelRequest request, ILocalModelSetupService setupService, CancellationToken cancellationToken) =>
        {
            await setupService.PullModelAsync(request.Name, cancellationToken);
            return Results.Ok(setupService.GetSnapshot());
        });

        app.MapGet("/openai", (IOptions<LocalAiOptions> options) => Results.Ok(new
        {
            BaseUrl = new Uri(new Uri(options.Value.BaseUrl), "/v1/").ToString(),
            ApiKey = "ollama",
            LocalOnly = options.Value.RequireLoopback,
            Supports = new[]
            {
                "chat/completions",
                "responses",
                "embeddings",
                "tools/function-calling for compatible local models",
                "vision for compatible local models"
            }
        }));

        app.MapPost("/chat", async (LocalChatRequest request, ILocalChatClient chatClient, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await chatClient.ChatAsync(request, cancellationToken));
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new ErrorResponse(exception.Message));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(exception.Message);
            }
        });

        return app;
    }
}
