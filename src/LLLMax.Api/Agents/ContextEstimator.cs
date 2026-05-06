namespace LLLMax.Api.Agents;

using LLLMax.Api.Models;

public static class ContextEstimator
{
    public static int EstimateTokens(string text) => string.IsNullOrWhiteSpace(text)
        ? 0
        : Math.Max(1, text.Length / 4);

    public static int EstimateTokens(IEnumerable<LocalChatMessage> messages) =>
        messages.Sum(message => EstimateTokens(message.Content));
}
