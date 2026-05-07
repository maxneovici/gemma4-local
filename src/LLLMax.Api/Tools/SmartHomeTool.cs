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

    public override string Description => "Control configured smart-home devices on the local network. For Philips Hue, use device=lights, operation=on/off, and target=all to turn every configured light on or off.";

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

        return new LocalToolResult($"Hue lights turned {(on ? "on" : "off")}.");
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

            return hue.LightIds.Values.ToList();
        }

        if (hue.LightIds.TryGetValue(target, out var configuredId))
        {
            return [configuredId];
        }

        if (Guid.TryParse(target, out _))
        {
            return [target];
        }

        throw new InvalidOperationException($"Hue light '{target}' is not configured.");
    }

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
                return new LocalToolResult("Samsung TV wake packet sent.");
            case "off" or "turn_off":
                await SendSamsungTvKeyAsync(tv, "KEY_POWER", cancellationToken);
                return new LocalToolResult("Samsung TV power-off command sent.");
            case "power" or "toggle_power" or "toggle":
                await SendSamsungTvKeyAsync(tv, "KEY_POWER", cancellationToken);
                return new LocalToolResult("Samsung TV power-toggle command sent.");
            default:
                throw new ArgumentException("Samsung TV operation must be on, off, or toggle_power.");
        }
    }

    private async Task SendSamsungTvKeyAsync(LocalSamsungTvOptions tv, string key, CancellationToken cancellationToken)
    {
        var encodedName = Convert.ToBase64String(Encoding.UTF8.GetBytes(tv.RemoteName));
        var uriBuilder = new UriBuilder("ws", tv.Host, tv.RemotePort, "/api/v2/channels/samsung.remote.control")
        {
            Query = string.IsNullOrWhiteSpace(tv.Token)
                ? $"name={Uri.EscapeDataString(encodedName)}"
                : $"name={Uri.EscapeDataString(encodedName)}&token={Uri.EscapeDataString(tv.Token)}"
        };

        using var socket = new ClientWebSocket();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.SmartHome.RequestTimeoutSeconds));
        await socket.ConnectAsync(uriBuilder.Uri, timeout.Token);

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
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

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
