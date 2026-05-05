using Gemma4Local.Api.Options;
using Microsoft.Extensions.Options;

namespace Gemma4Local.Api.Services;

public sealed class LocalEndpointGuard(IOptions<LocalAiOptions> options)
{
    private readonly LocalAiOptions _options = options.Value;

    public void ThrowIfRemoteEndpoint()
    {
        if (_options.RequireLoopback && !IsLoopback(_options.BaseUrl))
        {
            throw new InvalidOperationException("Remote AI endpoints are disabled. Configure LocalAi:BaseUrl to localhost or a loopback address.");
        }
    }

    public static bool IsLoopback(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }
}
