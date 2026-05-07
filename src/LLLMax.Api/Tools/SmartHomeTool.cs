using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LLLMax.Api.Options;
using Microsoft.Extensions.Options;

namespace LLLMax.Api.Tools;

public sealed class SmartHomeTool(IHttpClientFactory httpClientFactory, IOptions<LocalAiOptions> options) : LocalToolBase<SmartHomeArguments>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalAiOptions _options = options.Value;

    public override string Name => "smart_home";

    public override string Description => "Control configured smart-home devices on the local network. For Philips Hue, use device=lights, operation=on/off, and target=all to turn every configured non-excluded light on or off. For Samsung TV, use device=tv with operation=on/off/toggle_power/mute.";

    protected override async Task<LocalToolResult> InvokeAsync(SmartHomeArguments arguments, LocalToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!_options.SmartHome.Enabled)
        {
            throw new InvalidOperationException("Smart-home operations are disabled.");
        }

        var device = Normalize(arguments.Device);
        var operation = Normalize(arguments.Operation);

        return device switch
        {
            "hue" or "light" or "lights" => await InvokeHueAsync(arguments, operation, cancellationToken),
            "samsung_tv" or "samsungtv" or "tv" => await InvokeSamsungTvAsync(operation, cancellationToken),
            _ => throw new ArgumentException("Device must be one of: hue, light, lights, samsung_tv, tv.")
        };
    }

    private async Task<LocalToolResult> InvokeHueAsync(SmartHomeArguments arguments, string operation, CancellationToken cancellationToken)
    {
        var hue = _options.SmartHome.Hue;

        if (string.IsNullOrWhiteSpace(hue.BridgeHost) || string.IsNullOrWhiteSpace(hue.ApplicationKey))
        {
            throw new InvalidOperationException("Hue bridge is not configured. Set LocalAi:SmartHome:Hue:BridgeHost and ApplicationKey.");
        }

        var target = string.IsNullOrWhiteSpace(arguments.Target) ? "all" : arguments.Target.Trim();
        var lightIds = ResolveHueLightIds(hue, target);
        var on = operation switch
        {
            "on" or "turn_on" => true,
            "off" or "turn_off" => false,
            _ => throw new ArgumentException("Hue operation must be explicit: on or off.")
        };

        if (lightIds.Count == 1)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(BuildHueBaseUri(hue.BridgeHost), $"clip/v2/resource/light/{lightIds.Single()}"));
            request.Headers.TryAddWithoutValidation("hue-application-key", hue.ApplicationKey);
            request.Content = new StringContent(JsonSerializer.Serialize(new { on = new { on } }, JsonOptions), Encoding.UTF8, "application/json");
            await SendHueRequestAsync(request, cancellationToken);
            return new LocalToolResult($"Hue light '{target}' turned {(on ? "on" : "off")}.");
        }

        foreach (var lightId in lightIds)
        {
            using var multiRequest = new HttpRequestMessage(HttpMethod.Put, new Uri(BuildHueBaseUri(hue.BridgeHost), $"clip/v2/resource/light/{lightId}"));
            multiRequest.Headers.TryAddWithoutValidation("hue-application-key", hue.ApplicationKey);
            multiRequest.Content = new StringContent(JsonSerializer.Serialize(new { on = new { on } }, JsonOptions), Encoding.UTF8, "application/json");
            await SendHueRequestAsync(multiRequest, cancellationToken);
        }

        var excludedCount = CountExcludedConfiguredLights(hue);
        var exclusionNote = excludedCount == 0 ? string.Empty : $" Excluded {excludedCount} protected light(s).";

        return new LocalToolResult($"Hue lights turned {(on ? "on" : "off")}.{exclusionNote}");
    }

    private async Task SendHueRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await httpClientFactory.CreateClient("smart-home-hue").SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Hue bridge returned {(int)response.StatusCode}: {body}");
        }
    }

    private static Uri BuildHueBaseUri(string bridgeHost)
    {
        var host = bridgeHost.Trim();
        var uriText = host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? host
            : $"https://{host}";

        if (!Uri.TryCreate(uriText.EndsWith('/') ? uriText : $"{uriText}/", UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !IsPrivateOrLocalHost(uri.Host))
        {
            throw new InvalidOperationException("Hue bridge host must be a local/private HTTP(S) endpoint.");
        }

        return uri;
    }

    private static IReadOnlyList<string> ResolveHueLightIds(LocalHueOptions hue, string target)
    {
        if (target.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (hue.LightIds.Count == 0)
            {
                throw new InvalidOperationException("No Hue light IDs are configured.");
            }

            var lightIds = hue.LightIds
                .Where(pair => !IsExcludedHueLight(hue, pair.Key, pair.Value))
                .Select(pair => pair.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (lightIds.Count == 0)
            {
                throw new InvalidOperationException("No non-excluded Hue light IDs are configured.");
            }

            return lightIds;
        }

        if (hue.LightIds.TryGetValue(target, out var configuredId))
        {
            ThrowIfExcludedHueLight(hue, target, configuredId);
            return [configuredId];
        }

        if (Guid.TryParse(target, out _))
        {
            ThrowIfExcludedHueLight(hue, target, target);
            return [target];
        }

        throw new InvalidOperationException($"Hue light '{target}' is not configured.");
    }

    private static void ThrowIfExcludedHueLight(LocalHueOptions hue, string alias, string lightId)
    {
        if (IsExcludedHueLight(hue, alias, lightId))
        {
            throw new InvalidOperationException($"Hue light '{alias}' is excluded from smart_home control.");
        }
    }

    private static bool IsExcludedHueLight(LocalHueOptions hue, string alias, string lightId) =>
        hue.ExcludedLightAliases.Contains(alias, StringComparer.OrdinalIgnoreCase)
        || hue.ExcludedLightIds.Contains(lightId, StringComparer.OrdinalIgnoreCase);

    private static int CountExcludedConfiguredLights(LocalHueOptions hue) =>
        hue.LightIds.Count(pair => IsExcludedHueLight(hue, pair.Key, pair.Value));

    private async Task<LocalToolResult> InvokeSamsungTvAsync(string operation, CancellationToken cancellationToken)
    {
        var tv = _options.SmartHome.SamsungTv;

        if (string.IsNullOrWhiteSpace(tv.Host))
        {
            throw new InvalidOperationException("Samsung TV host is not configured. Set LocalAi:SmartHome:SamsungTv:Host.");
        }

        if (!IsPrivateOrLocalHost(tv.Host))
        {
            throw new InvalidOperationException("Samsung TV host must be a local/private endpoint.");
        }

        switch (operation)
        {
            case "on" or "turn_on":
                if (string.IsNullOrWhiteSpace(tv.MacAddress))
                {
                    throw new InvalidOperationException("Samsung TV wake requires LocalAi:SmartHome:SamsungTv:MacAddress.");
                }

                await SendWakeOnLanAsync(tv.MacAddress, tv.WakePort, cancellationToken);
                return await SamsungTvPowerResultAsync(tv, "wake packet sent", "on", cancellationToken);
            case "off" or "turn_off":
                await SendSamsungTvKeyAsync(tv, "KEY_POWER", cancellationToken);
                return await SamsungTvPowerResultAsync(tv, "power command sent", "off", cancellationToken);
            case "power" or "toggle_power" or "toggle":
                await SendSamsungTvKeyAsync(tv, "KEY_POWER", cancellationToken);
                return new LocalToolResult("Samsung TV power-toggle command sent. TV power state was not verified.");
            case "mute" or "unmute" or "toggle_mute":
                await SendSamsungTvKeyAsync(tv, "KEY_MUTE", cancellationToken);
                return new LocalToolResult("Samsung TV mute-toggle command sent. Samsung exposes mute as a toggle, so mute and unmute use the same command.");
            default:
                throw new ArgumentException("Samsung TV operation must be on, off, toggle_power, or mute.");
        }
    }

    private async Task<LocalToolResult> SamsungTvPowerResultAsync(LocalSamsungTvOptions tv, string commandDescription, string requestedState, CancellationToken cancellationToken)
    {
        var observedState = await WaitForSamsungTvPowerStateAsync(tv, requestedState, cancellationToken);

        if (requestedState.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            return observedState?.Equals("on", StringComparison.OrdinalIgnoreCase) == true
                ? new LocalToolResult("Samsung TV wake packet sent and PowerState is on.")
                : new LocalToolResult($"Samsung TV {commandDescription}, but PowerState was not verified as on. Observed PowerState: {observedState ?? "unreachable"}.");
        }

        if (observedState is null)
        {
            return new LocalToolResult($"Samsung TV {commandDescription}; TV became unreachable afterward, which may mean it powered off, but off state was not explicitly verified.");
        }

        return observedState.Equals("on", StringComparison.OrdinalIgnoreCase) == false
            ? new LocalToolResult($"Samsung TV {commandDescription} and PowerState is {observedState}.")
            : new LocalToolResult($"Samsung TV {commandDescription}, but PowerState was not verified as off. Observed PowerState: {observedState}.");
    }

    private async Task<string?> WaitForSamsungTvPowerStateAsync(LocalSamsungTvOptions tv, string requestedState, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, _options.SmartHome.RequestTimeoutSeconds));
        string? lastState = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            lastState = await GetSamsungTvPowerStateAsync(tv, cancellationToken);

            if (requestedState.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                if (lastState?.Equals("on", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return lastState;
                }
            }
            else if (lastState is not null && !lastState.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                return lastState;
            }

            await Task.Delay(500, cancellationToken);
        }

        return lastState;
    }

    private async Task<string?> GetSamsungTvPowerStateAsync(LocalSamsungTvOptions tv, CancellationToken cancellationToken)
    {
        try
        {
            var uri = new UriBuilder("http", tv.Host, tv.RemotePort, "/api/v2/").Uri;
            var json = await httpClientFactory.CreateClient("smart-home-hue").GetStringAsync(uri, cancellationToken);
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.TryGetProperty("device", out var device)
                && device.TryGetProperty("PowerState", out var powerState)
                && powerState.GetString() is { Length: > 0 } parsed)
            {
                return parsed;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }

        return null;
    }

    private async Task SendSamsungTvKeyAsync(LocalSamsungTvOptions tv, string key, CancellationToken cancellationToken)
    {
        var encodedName = Convert.ToBase64String(Encoding.UTF8.GetBytes(tv.RemoteName));
        var uriBuilder = new UriBuilder(tv.RemotePort == 8002 ? "wss" : "ws", tv.Host, tv.RemotePort, "/api/v2/channels/samsung.remote.control")
        {
            Query = string.IsNullOrWhiteSpace(tv.Token)
                ? $"name={Uri.EscapeDataString(encodedName)}"
                : $"name={Uri.EscapeDataString(encodedName)}&token={Uri.EscapeDataString(tv.Token)}"
        };

        using var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.SmartHome.RequestTimeoutSeconds));
        await socket.ConnectAsync(uriBuilder.Uri, timeout.Token);
        await WaitForSamsungTvRemoteReadyAsync(socket, timeout.Token);

        var payload = JsonSerializer.Serialize(new
        {
            method = "ms.remote.control",
            @params = new
            {
                Cmd = "Click",
                DataOfCmd = key,
                Option = "false",
                TypeOfRemote = "SendRemoteKey"
            }
        }, JsonOptions);

        await socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, timeout.Token);
        await ThrowIfSamsungTvRemoteRejectedAsync(socket, timeout.Token);

        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
            }
            catch (WebSocketException)
            {
                // Samsung TVs often abort immediately after accepting a remote key.
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Closing is best-effort after the key has been sent.
            }
        }
    }

    private static async Task WaitForSamsungTvRemoteReadyAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        while (socket.State == WebSocketState.Open)
        {
            var message = await ReceiveSamsungTvMessageAsync(socket, cancellationToken);

            if (message is null)
            {
                continue;
            }

            var remoteEvent = GetSamsungTvEvent(message);

            if (remoteEvent?.Equals("ms.channel.connect", StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }

            if (IsSamsungTvRemoteError(remoteEvent))
            {
                throw new InvalidOperationException($"Samsung TV remote authorization failed: {message}");
            }
        }

        throw new InvalidOperationException("Samsung TV remote websocket closed before authorization completed.");
    }

    private static async Task ThrowIfSamsungTvRemoteRejectedAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(750));

        try
        {
            var message = await ReceiveSamsungTvMessageAsync(socket, timeout.Token);
            var remoteEvent = message is null ? null : GetSamsungTvEvent(message);

            if (IsSamsungTvRemoteError(remoteEvent))
            {
                throw new InvalidOperationException($"Samsung TV remote command was rejected: {message}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Samsung does not acknowledge successful remote keys.
        }
    }

    private static async Task<string?> ReceiveSamsungTvMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;

        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return result.MessageType == WebSocketMessageType.Text
            ? Encoding.UTF8.GetString(stream.ToArray())
            : null;
    }

    private static string? GetSamsungTvEvent(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            return document.RootElement.TryGetProperty("event", out var remoteEvent) ? remoteEvent.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsSamsungTvRemoteError(string? remoteEvent) =>
        remoteEvent?.Equals("ms.error", StringComparison.OrdinalIgnoreCase) == true
        || remoteEvent?.Equals("ms.channel.unauthorized", StringComparison.OrdinalIgnoreCase) == true;

    private static async Task SendWakeOnLanAsync(string macAddress, int port, CancellationToken cancellationToken)
    {
        var macBytes = PhysicalAddress.Parse(macAddress.Replace(":", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal)).GetAddressBytes();
        var packet = Enumerable.Repeat((byte)0xff, 6)
            .Concat(Enumerable.Range(0, 16).SelectMany(_ => macBytes))
            .ToArray();

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.EnableBroadcast = true;
        await udp.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, port));
    }

    private static bool IsPrivateOrLocalHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || bytes[0] == 127
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254);
    }

    private static string Normalize(string value) => value.Trim().Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
}

public sealed record SmartHomeArguments(
    [property: Required] string Device,
    [property: Required] string Operation,
    string? Target = null);
