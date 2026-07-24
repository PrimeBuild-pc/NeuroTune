using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NeuroTune;

public sealed class OpenRouterOAuthService
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<string> SignInAsync(CancellationToken cancellationToken = default)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var port = FreeLoopbackPort();
        var callback = $"http://127.0.0.1:{port}/callback";

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var authUrl = "https://openrouter.ai/auth" +
            $"?callback_url={Uri.EscapeDataString(callback)}" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}" +
            "&code_challenge_method=S256";
        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            var context = await listener.GetContextAsync().WaitAsync(remaining, cancellationToken);
            if (!string.Equals(context.Request.Url?.AbsolutePath, "/callback", StringComparison.OrdinalIgnoreCase))
            {
                await Respond(context.Response, 404, "Not found", "Return to NeuroTune and try again.");
                continue;
            }

            var error = context.Request.QueryString["error"];
            if (!string.IsNullOrWhiteSpace(error))
            {
                await Respond(context.Response, 400, "Authorization cancelled", "You can close this tab and return to NeuroTune.");
                throw new InvalidOperationException("OpenRouter browser authorization was cancelled.");
            }

            var returnedState = context.Request.QueryString["state"] ?? "";
            var code = context.Request.QueryString["code"] ?? "";
            if (!SecureEquals(state, returnedState) || code.Length is < 8 or > 1_024)
            {
                await Respond(context.Response, 400, "Authorization rejected", "The callback was invalid. Return to NeuroTune and try again.");
                throw new InvalidOperationException("OpenRouter returned an invalid OAuth callback.");
            }

            await Respond(context.Response, 200, "Connected to NeuroTune", "You can close this tab and return to the app.");
            return await ExchangeCode(code, verifier, cancellationToken);
        }
        throw new TimeoutException("OpenRouter browser authorization timed out.");
    }

    private static async Task<string> ExchangeCode(string code, string verifier, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/auth/keys")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                code,
                code_verifier = verifier,
                code_challenge_method = "S256"
            }), Encoding.UTF8, "application/json")
        };
        using var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenRouter key exchange failed with HTTP {(int)response.StatusCode}.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var key = json.RootElement.TryGetProperty("key", out var property) ? property.GetString() : null;
        return !string.IsNullOrWhiteSpace(key)
            ? key
            : throw new InvalidOperationException("OpenRouter did not return an API key.");
    }

    private static async Task Respond(HttpListenerResponse response, int status, string title, string message)
    {
        var html = $$"""
            <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
            <style>body{font-family:system-ui;display:grid;place-items:center;min-height:100vh;margin:0;background:#0b0e14;color:#f5f7fa}main{max-width:420px;padding:32px;text-align:center}p{color:#a9b4c3}</style></head>
            <body><main><h1>{{WebUtility.HtmlEncode(title)}}</h1><p>{{WebUtility.HtmlEncode(message)}}</p></main></body></html>
            """;
        var bytes = Encoding.UTF8.GetBytes(html);
        response.StatusCode = status;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static int FreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool SecureEquals(string expected, string actual)
    {
        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(actual);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
