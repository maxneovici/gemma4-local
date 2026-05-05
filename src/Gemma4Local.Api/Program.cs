using System.Text.Json.Serialization;
using Gemma4Local.Api.Endpoints;
using Gemma4Local.Api.Options;
using Gemma4Local.Api.Agents;
using Gemma4Local.Api.Memory;
using Gemma4Local.Api.Services;
using Gemma4Local.Api.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services
    .AddOptions<LocalAiOptions>()
    .Bind(builder.Configuration.GetSection(LocalAiOptions.SectionName))
    .Validate(options => !options.RequireLoopback || LocalEndpointGuard.IsLoopback(options.BaseUrl),
        "LocalAi:BaseUrl must point to localhost/loopback when LocalAi:RequireLoopback is enabled.")
    .ValidateOnStart();

builder.Services.AddHttpClient("ollama", (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalAiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
});

builder.Services.AddSingleton<LocalEndpointGuard>();
builder.Services.AddSingleton<IOllamaApi, OllamaApi>();
builder.Services.AddSingleton<ILocalModelSetupService, LocalModelSetupService>();
builder.Services.AddSingleton<ILocalChatClient, OllamaLocalChatClient>();
builder.Services.AddSingleton<IAgentRegistry, ConfiguredAgentRegistry>();
builder.Services.AddSingleton<IAgentRuntime, AgentRuntime>();
builder.Services.AddSingleton<ILocalToolRegistry, LocalToolRegistry>();
builder.Services.AddSingleton<ILocalTool, AgentDelegationTool>();
builder.Services.AddSingleton<ILocalTool, MemorySearchTool>();
builder.Services.AddSingleton<ILocalTool, MemoryWriteTool>();
builder.Services.AddSingleton<ILocalTool, SafeShellCommandTool>();
builder.Services.AddSingleton<IEmbeddingGenerator, OllamaEmbeddingGenerator>();
builder.Services.AddSingleton<ILocalMemoryStore, InMemoryVectorStore>();
builder.Services.AddHostedService<OllamaProcessHostedService>();
builder.Services.AddHostedService<LocalModelSetupHostedService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.DocumentTitle = "Local AI Baseline";
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Local AI Baseline v1");
});

app.MapLocalAiEndpoints();
app.MapAgentEndpoints();
app.MapMemoryEndpoints();

app.Run();

public partial class Program;
