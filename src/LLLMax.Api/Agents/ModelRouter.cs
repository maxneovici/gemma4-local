using LLLMax.Api.Options;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Agents;

public interface IModelRouter
{
    ModelRoute Resolve(ModelRouteRequest request);
}

public sealed class ModelRouter(IOptions<LocalAiOptions> options) : IModelRouter
{
    private readonly LocalAiOptions _options = options.Value;

    public ModelRoute Resolve(ModelRouteRequest request)
    {
        var effort = NormalizeEffort(request.ReasoningEffort, request.Message);
        var model = request.Model ?? request.Agent.Model ?? effort switch
        {
            "low" => _options.ModelRouter.InteractiveModel ?? _options.DefaultModel,
            "high" => _options.ModelRouter.DeepReasoningModel ?? _options.ModelRouter.BalancedModel ?? _options.DefaultModel,
            _ => _options.ModelRouter.BalancedModel ?? _options.DefaultModel
        };

        return new ModelRoute(model, effort, EnableThinking(effort), Temperature(effort));
    }

    private string NormalizeEffort(string? requested, string message)
    {
        var value = string.IsNullOrWhiteSpace(requested) ? _options.ModelRouter.DefaultReasoningEffort : requested.Trim().ToLowerInvariant();

        if (value is "low" or "medium" or "high")
        {
            return value;
        }

        var wordCount = message.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var isConversational = message.EndsWith('?') && wordCount <= _options.ModelRouter.ShortRequestWordThreshold;

        return wordCount <= _options.ModelRouter.ShortRequestWordThreshold || isConversational ? "low" : "medium";
    }

    private static bool EnableThinking(string effort) => effort is "medium" or "high";

    private static double Temperature(string effort) => effort switch
    {
        "low" => 0.4,
        "high" => 0.8,
        _ => 0.6
    };
}

public sealed record ModelRouteRequest(AgentDefinition Agent, string Message, string? Model, string? ReasoningEffort);

public sealed record ModelRoute(string Model, string ReasoningEffort, bool EnableThinking, double Temperature);
