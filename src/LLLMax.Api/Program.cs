using System.Text.Json.Serialization;
using LLLMax.Api.Endpoints;
using LLLMax.Api.Options;
using LLLMax.Api.Agents;
using LLLMax.Api.Approvals;
using LLLMax.Api.BackgroundJobs;
using LLLMax.Api.Documents;
using LLLMax.Api.Integrations;
using LLLMax.Api.Mcp;
using LLLMax.Api.Memory;
using LLLMax.Api.Services;
using LLLMax.Api.SelfImprovement;
using LLLMax.Api.Sessions;
using LLLMax.Api.Skills;
using LLLMax.Api.Storage;
using LLLMax.Api.Tasks;
using LLLMax.Api.Tools;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

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

builder.Services.AddHttpClient("qdrant", (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalAiOptions>>().Value;
    client.BaseAddress = new Uri(options.Memory.QdrantBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
});

builder.Services.AddHttpClient("smart-home-hue", (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalAiOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(options.SmartHome.RequestTimeoutSeconds);
}).ConfigurePrimaryHttpMessageHandler(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalAiOptions>>().Value;

    return new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = options.SmartHome.Hue.IgnoreCertificateErrors
            ? HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            : null
    };
});

builder.Services.AddSingleton<LocalDataPaths>();
builder.Services.AddSingleton<LocalEndpointGuard>();
builder.Services.AddSingleton<WorkspaceToolSupport>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalAiOptions>>().Value;
    return new WorkspaceToolSupport(options, serviceProvider.GetRequiredService<IWebHostEnvironment>());
});
builder.Services.AddDbContextFactory<LocalDbContext>((serviceProvider, options) =>
{
    var paths = serviceProvider.GetRequiredService<LocalDataPaths>();
    options.UseSqlite($"Data Source={paths.SqliteDatabasePath}");
});
builder.Services.AddSingleton<IApprovalStore, EfApprovalStore>();
builder.Services.AddSingleton<IApprovalService, ApprovalService>();
builder.Services.AddSingleton<IMcpRegistry, EfMcpRegistry>();
builder.Services.AddSingleton<IMcpBridge, McpBridge>();
builder.Services.AddSingleton<IOllamaApi, OllamaApi>();
builder.Services.AddSingleton<ILocalModelSetupService, LocalModelSetupService>();
builder.Services.AddSingleton<ILocalChatClient, OllamaLocalChatClient>();
builder.Services.AddSingleton<IRuntimeModelSettings, RuntimeModelSettings>();
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
builder.Services.AddSingleton<ILocalTool, ScheduleBackgroundJobTool>();
builder.Services.AddSingleton<ILocalTool, OcrTool>();
builder.Services.AddSingleton<ILocalTool, InvoiceExtractionTool>();
builder.Services.AddSingleton<ILocalTool, SafeShellCommandTool>();
builder.Services.AddSingleton<ILocalTool, WorkspaceSearchTool>();
builder.Services.AddSingleton<ILocalTool, WorkspaceReadTool>();
builder.Services.AddSingleton<ILocalTool, WorkspaceWriteTool>();
builder.Services.AddSingleton<ILocalTool, GitInspectTool>();
builder.Services.AddSingleton<ILocalTool, PatchProposalTool>();
builder.Services.AddSingleton<ILocalTool, CreateSkillTool>();
builder.Services.AddSingleton<ILocalTool, UpdateSkillTool>();
builder.Services.AddSingleton<ILocalTool, SmartHomeTool>();
builder.Services.AddSingleton<IEmbeddingGenerator, OllamaEmbeddingGenerator>();
builder.Services.AddSingleton<ILocalMemoryStore>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalAiOptions>>().Value;
    return options.Memory.Provider switch
    {
        var provider when provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase) => new InMemoryVectorStore(serviceProvider.GetRequiredService<IEmbeddingGenerator>()),
        var provider when provider.Equals("Qdrant", StringComparison.OrdinalIgnoreCase) => new QdrantVectorStore(
            serviceProvider.GetRequiredService<IHttpClientFactory>(),
            serviceProvider.GetRequiredService<IEmbeddingGenerator>(),
            serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LocalAiOptions>>()),
        _ => new FileVectorStore(serviceProvider.GetRequiredService<IEmbeddingGenerator>(), serviceProvider.GetRequiredService<LocalDataPaths>())
    };
});
builder.Services.AddSingleton<IDocumentService, DocumentService>();
builder.Services.AddSingleton<IApiIntegrationRegistry, EfApiIntegrationRegistry>();
builder.Services.AddSingleton<IAssistantSessionStore, EfAssistantSessionStore>();
builder.Services.AddSingleton<PatchProposalStore>();
builder.Services.AddSingleton<IBackgroundJobQueue, ChannelBackgroundJobQueue>();
builder.Services.AddSingleton<IBackgroundJobStore, EfBackgroundJobStore>();
builder.Services.AddSingleton<IBackgroundJobArtifactStore, EfBackgroundJobArtifactStore>();
builder.Services.AddSingleton<BackgroundJobService>();
builder.Services.AddSingleton<IBackgroundJobService>(serviceProvider => serviceProvider.GetRequiredService<BackgroundJobService>());
builder.Services.AddSingleton<IBackgroundJobHandler, DocumentVectorizationJobHandler>();
builder.Services.AddSingleton<IBackgroundJobHandler, MemoryReportJobHandler>();
builder.Services.AddSingleton<IBackgroundJobHandler, WebResearchJobHandler>();
builder.Services.AddSingleton<IBackgroundJobHandler, MemoryConsolidationJobHandler>();
builder.Services.AddSingleton<ITaskGraphStore, EfTaskGraphStore>();
builder.Services.AddSingleton<ITaskGraphService, TaskGraphService>();
builder.Services.AddSingleton<IMemoryConsolidationJobStore, EfMemoryConsolidationJobStore>();
builder.Services.AddSingleton<IMemoryConsolidationService, MemoryConsolidationService>();
builder.Services.AddSingleton<ISkillRegistry, FileSkillRegistry>();
builder.Services.AddSingleton<IAssistantOrchestrator, AssistantOrchestrator>();
builder.Services.AddHostedService<OllamaProcessHostedService>();
builder.Services.AddHostedService<LocalModelSetupHostedService>();
builder.Services.AddHostedService<LocalDbHostedService>();
builder.Services.AddHostedService<BackgroundJobWorker>();

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
app.MapBackgroundJobEndpoints();
app.MapSelfImprovementEndpoints();
app.MapSkillEndpoints();
app.MapSessionEndpoints();
app.MapTaskGraphEndpoints();
app.MapDocumentEndpoints();
app.MapIntegrationEndpoints();
app.MapSmartHomeEndpoints();

app.Run();

public partial class Program;
