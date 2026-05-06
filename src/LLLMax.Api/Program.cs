using System.Text.Json.Serialization;
using LLLMax.Api.Endpoints;
using LLLMax.Api.Options;
using LLLMax.Api.Agents;
using LLLMax.Api.Approvals;
using LLLMax.Api.Documents;
using LLLMax.Api.Integrations;
using LLLMax.Api.Mcp;
using LLLMax.Api.Memory;
using LLLMax.Api.Services;
using LLLMax.Api.Sessions;
using LLLMax.Api.Tasks;
using LLLMax.Api.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAntiforgery();

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

builder.Services.AddHttpClient("web-browse", (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalAiOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(options.WebBrowsing.RequestTimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("LLLMax/0.1 local assistant");
});

builder.Services.AddHttpClient("integrations", (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalAiOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(options.ApiDiscovery.RequestTimeoutSeconds);
});

builder.Services.AddSingleton<LocalDataPaths>();
builder.Services.AddSingleton<LocalEndpointGuard>();
builder.Services.AddSingleton<IApprovalStore, FileApprovalStore>();
builder.Services.AddSingleton<IApprovalService, ApprovalService>();
builder.Services.AddSingleton<IMcpRegistry, FileMcpRegistry>();
builder.Services.AddSingleton<IMcpBridge, McpBridge>();
builder.Services.AddSingleton<IOllamaApi, OllamaApi>();
builder.Services.AddSingleton<ILocalModelSetupService, LocalModelSetupService>();
builder.Services.AddSingleton<ILocalChatClient, OllamaLocalChatClient>();
builder.Services.AddSingleton<IModelRouter, ModelRouter>();
builder.Services.AddSingleton<INativeToolChatClient, OllamaNativeToolChatClient>();
builder.Services.AddSingleton<IAgentRegistry, MarkdownAgentRegistry>();
builder.Services.AddSingleton<IAgentRuntime, AgentRuntime>();
builder.Services.AddSingleton<ILocalToolRegistry, LocalToolRegistry>();
builder.Services.AddSingleton<ILocalTool, AgentDelegationTool>();
builder.Services.AddSingleton<ILocalTool, MemorySearchTool>();
builder.Services.AddSingleton<ILocalTool, MemoryWriteTool>();
builder.Services.AddSingleton<ILocalTool, WebBrowseTool>();
builder.Services.AddSingleton<ILocalTool, ApiIntegrationTool>();
builder.Services.AddSingleton<ILocalTool, DocumentVectorizeTool>();
builder.Services.AddSingleton<ILocalTool, OcrTool>();
builder.Services.AddSingleton<ILocalTool, InvoiceExtractionTool>();
builder.Services.AddSingleton<ILocalTool, SafeShellCommandTool>();
builder.Services.AddSingleton<IEmbeddingGenerator, OllamaEmbeddingGenerator>();
builder.Services.AddSingleton<ILocalMemoryStore>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalAiOptions>>().Value;
    return options.Memory.Provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase)
        ? new InMemoryVectorStore(serviceProvider.GetRequiredService<IEmbeddingGenerator>())
        : new FileVectorStore(serviceProvider.GetRequiredService<IEmbeddingGenerator>(), serviceProvider.GetRequiredService<LocalDataPaths>());
});
builder.Services.AddSingleton<IDocumentService, DocumentService>();
builder.Services.AddSingleton<IApiIntegrationRegistry, ApiIntegrationRegistry>();
builder.Services.AddSingleton<IAssistantSessionStore, FileAssistantSessionStore>();
builder.Services.AddSingleton<ITaskGraphStore, FileTaskGraphStore>();
builder.Services.AddSingleton<ITaskGraphService, TaskGraphService>();
builder.Services.AddSingleton<IMemoryConsolidationService, MemoryConsolidationService>();
builder.Services.AddSingleton<IAssistantOrchestrator, AssistantOrchestrator>();
builder.Services.AddHostedService<OllamaProcessHostedService>();
builder.Services.AddHostedService<LocalModelSetupHostedService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.DocumentTitle = "LLLMax";
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "LLLMax v1");
});

app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.MapLocalAiEndpoints();
app.MapAgentEndpoints();
app.MapApprovalEndpoints();
app.MapMcpEndpoints();
app.MapMemoryEndpoints();
app.MapSessionEndpoints();
app.MapTaskGraphEndpoints();
app.MapDocumentEndpoints();
app.MapIntegrationEndpoints();

app.Run();

public partial class Program;
