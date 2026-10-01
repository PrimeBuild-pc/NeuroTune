using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NeuroTune;

public sealed record ChatGptAccountInfo(string Id, string Label, bool Connected, bool PlanEnabled);
public sealed record ChatGptSignInRequest(string? AccountId);

public sealed class ChatGptOAuthService
{
    private const string Issuer = "https://auth.openai.com";
    private const string Resource = "https://api.openai.com/v1";
    private const string TokenEndpoint = Issuer + "/api/accounts/oauth/token";
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly string StorePath = Path.Combine(SettingsService.DataDirectory, "chatgpt-accounts.dpapi");

    public IReadOnlyList<ChatGptAccountInfo> Accounts() => Load().Accounts.Select(account => new ChatGptAccountInfo(
        account.Id, $"{account.Email ?? "ChatGPT account"} · {account.ClientId[^Math.Min(8, account.ClientId.Length)..]}",
        !string.IsNullOrEmpty(account.AccessToken) && PlanEnabled(account.Scopes), PlanEnabled(account.Scopes))).ToList();

    public async Task<string> SignInAsync(string? accountId, CancellationToken cancellationToken = default)
    {
        using var gate = await LockAsync(cancellationToken);
        var store = Load();
        var previous = accountId is null ? null : Find(store, accountId);
        if (previous is null && store.Accounts.Count >= 32) throw new InvalidOperationException("At most 32 ChatGPT registrations can be stored.");
        var discovery = await ReadJsonAsync(Issuer + "/.well-known/openid-configuration", cancellationToken);
        using (discovery)
        {
            if (discovery.RootElement.GetProperty("issuer").GetString() != Issuer ||
                discovery.RootElement.GetProperty("authorization_endpoint").GetString() != Issuer + "/api/accounts/authorize" ||
                discovery.RootElement.GetProperty("token_endpoint").GetString() != TokenEndpoint ||
                discovery.RootElement.GetProperty("jwks_uri").GetString() != Issuer + "/.well-known/jwks.json")
                throw new InvalidOperationException("OpenAI discovery changed; review the authorization endpoints before connecting.");
        }
        if (string.IsNullOrEmpty(store.HostId)) { store.HostId = $"urn:uuid:{Guid.NewGuid():D}"; Save(store); }
        var state = OpenRouterOAuthService.Base64Url(RandomNumberGenerator.GetBytes(32));
        var nonce = OpenRouterOAuthService.Base64Url(RandomNumberGenerator.GetBytes(32));
        var verifier = OpenRouterOAuthService.Base64Url(RandomNumberGenerator.GetBytes(32));
        var port = OpenRouterOAuthService.FreeLoopbackPort();
        var callback = $"http://127.0.0.1:{port}/auth/callback";
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = previous?.ClientId ?? "dynamic_agent_client",
            ["ext_agent_host_id"] = store.HostId,
            ["response_type"] = "code",
            ["redirect_uri"] = callback,
            ["scope"] = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct",
            ["resource"] = Resource,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = OpenRouterOAuthService.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
        };
        if (previous is null) parameters["agent_name_hint"] = "NeuroTune";
        else
        {
            if (!string.IsNullOrEmpty(previous.IdToken)) parameters["id_token_hint"] = previous.IdToken;
            if (!string.IsNullOrEmpty(previous.Email)) parameters["login_hint"] = previous.Email;
            if (!PlanEnabled(previous.Scopes)) parameters["prompt"] = "consent";
        }
        // This URL can contain an ID-token hint: never log it or send it to the frontend.
        var url = Issuer + "/api/accounts/authorize?" + string.Join("&", parameters.Select(item => $"{item.Key}={Uri.EscapeDataString(item.Value)}"));
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var context = await listener.GetContextAsync().WaitAsync(deadline - DateTimeOffset.UtcNow, cancellationToken);
            if (context.Request.HttpMethod != "GET" || context.Request.Url?.AbsolutePath != "/auth/callback")
            {
                await OpenRouterOAuthService.Respond(context.Response, 404, "Not found", "Return to NeuroTune.");
                continue;
            }
            var query = context.Request.QueryString;
            if (!OpenRouterOAuthService.SecureEquals(state, query["state"] ?? ""))
            {
                await OpenRouterOAuthService.Respond(context.Response, 400, "Authorization rejected", "The callback state was invalid.");
                throw new InvalidOperationException("OpenAI returned an invalid OAuth state.");
            }
            if (!string.IsNullOrEmpty(query["error"]))
            {
                await OpenRouterOAuthService.Respond(context.Response, 400, "Authorization cancelled", "No existing connection was replaced.");
                throw new InvalidOperationException("ChatGPT authorization was declined or failed. Existing registrations were preserved.");
            }
            var clientId = ValidateCallbackClient(previous?.ClientId, query["client_id"]);
            var code = query["code"];
            if (string.IsNullOrEmpty(code) || code.Length > 4096) throw new InvalidOperationException("OpenAI returned an invalid authorization code.");
            await OpenRouterOAuthService.Respond(context.Response, 200, "Authorization received", "Return to NeuroTune while it verifies your account and permissions.");
            using var tokens = await ExchangeAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["redirect_uri"] = callback,
                ["resource"] = Resource
            }, cancellationToken);
            var receivedAt = DateTimeOffset.UtcNow;
            using var jwks = await ReadJsonAsync(Issuer + "/.well-known/jwks.json", cancellationToken);
            var identity = ValidateIdToken(tokens.RootElement.GetProperty("id_token").GetString() ?? "", jwks.RootElement, clientId, nonce, DateTimeOffset.UtcNow);
            if (previous is not null && previous.Subject != identity.Subject) throw new InvalidOperationException("ChatGPT returned a different account; add it separately instead of replacing this registration.");
            var account = previous ?? store.Accounts.SingleOrDefault(item => item.ClientId == clientId && item.Subject == identity.Subject)
                ?? new Account { Id = Guid.NewGuid().ToString("D"), ClientId = clientId, Subject = identity.Subject };
            ApplyTokens(account, tokens.RootElement, identity.Email, false, receivedAt);
            if (!store.Accounts.Contains(account)) store.Accounts.Add(account);
            Save(store);
            return account.Id;
        }
        throw new TimeoutException("ChatGPT browser authorization timed out.");
    }

    public async Task<string> AccessTokenAsync(string? accountId, CancellationToken cancellationToken = default)
    {
        using var gate = await LockAsync(cancellationToken);
        var store = Load();
        var account = Find(store, accountId);
        if (!PlanEnabled(account.Scopes)) throw new InvalidOperationException("ChatGPT plan usage is not authorized. Continue with ChatGPT to enable it, or explicitly select API-key billing.");
        if (string.IsNullOrEmpty(account.AccessToken) || string.IsNullOrEmpty(account.RefreshToken)) throw new InvalidOperationException("Sign in with ChatGPT again.");
        var now = DateTimeOffset.UtcNow;
        if (account.ExpiresAt > now.AddSeconds(30) || account.ExpiresAt > now && account.EarliestRefreshAt > now) return account.AccessToken;
        try
        {
            using var tokens = await ExchangeAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = account.ClientId,
                ["refresh_token"] = account.RefreshToken,
                ["resource"] = Resource
            }, cancellationToken);
            var receivedAt = DateTimeOffset.UtcNow;
            if (tokens.RootElement.TryGetProperty("id_token", out var idToken))
            {
                using var jwks = await ReadJsonAsync(Issuer + "/.well-known/jwks.json", cancellationToken);
                var identity = ValidateIdToken(idToken.GetString() ?? "", jwks.RootElement, account.ClientId, null, DateTimeOffset.UtcNow);
                if (identity.Subject != account.Subject) throw new InvalidOperationException("The refreshed ChatGPT identity did not match the selected account.");
            }
            ApplyTokens(account, tokens.RootElement, account.Email, true, receivedAt);
            Save(store);
            if (!PlanEnabled(account.Scopes)) throw new InvalidOperationException("ChatGPT plan permission is no longer granted. Sign in again to authorize it.");
            return account.AccessToken!;
        }
        catch (InvalidOperationException error) when (error.Data["oauthCode"] is string code && IsTerminalRefreshError(code))
        {
            ClearTokens(account); Save(store);
            throw new InvalidOperationException("The ChatGPT session is no longer usable. Sign in again; its registered client has been retained.");
        }
    }

    public async Task<bool> SignOutAsync(string? accountId, CancellationToken cancellationToken = default)
    {
        using var gate = await LockAsync(cancellationToken);
        var store = Load();
        var account = Find(store, accountId);
        var revoked = false;
        if (!string.IsNullOrEmpty(account.RefreshToken))
        {
            try
            {
                using var response = await Http.PostAsync(Issuer + "/api/accounts/oauth/revoke", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = account.RefreshToken!,
                    ["token_type_hint"] = "refresh_token",
                    ["client_id"] = account.ClientId
                }), cancellationToken);
                revoked = response.StatusCode == HttpStatusCode.OK;
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException) { }
        }
        ClearTokens(account); Save(store);
        return revoked;
    }

    internal static string ValidateCallbackClient(string? expected, string? returned)
    {
        if (expected is not null && returned is not null && expected != returned) throw new InvalidOperationException("OpenAI returned a different registered client.");
        var id = returned ?? expected;
        if (string.IsNullOrEmpty(id) || !id.StartsWith("oaiapp_", StringComparison.Ordinal) || id.Length > 256 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-')))
            throw new InvalidOperationException("OpenAI did not return a valid issued client ID.");
        return id;
    }

    internal sealed record Identity(string Subject, string? Email);
    internal static Identity ValidateIdToken(string token, JsonElement jwks, string clientId, string? nonce, DateTimeOffset now)
    {
        try
        {
            if (token.Length > 32768) throw new InvalidOperationException();
            var parts = token.Split('.');
            if (parts.Length != 3) throw new InvalidOperationException();
            using var header = JsonDocument.Parse(Decode(parts[0]));
            if (header.RootElement.GetProperty("alg").GetString() != "RS256" || header.RootElement.TryGetProperty("crit", out _) ||
                header.RootElement.TryGetProperty("b64", out var encoding) && encoding.ValueKind != JsonValueKind.True) throw new InvalidOperationException();
            var kid = header.RootElement.GetProperty("kid").GetString();
            if (string.IsNullOrEmpty(kid)) throw new InvalidOperationException();
            var key = jwks.GetProperty("keys").EnumerateArray().Single(key => key.GetProperty("kid").GetString() == kid);
            if (key.GetProperty("kty").GetString() != "RSA" || key.TryGetProperty("alg", out var algorithm) && algorithm.GetString() != "RS256" || key.TryGetProperty("use", out var use) && use.GetString() != "sig") throw new InvalidOperationException();
            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters { Modulus = Decode(key.GetProperty("n").GetString()!), Exponent = Decode(key.GetProperty("e").GetString()!) });
            if (rsa.KeySize < 2048 || !rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new InvalidOperationException();
            using var payload = JsonDocument.Parse(Decode(parts[1]));
            var claims = payload.RootElement;
            var audience = claims.GetProperty("aud");
            var audienceMatches = audience.ValueKind == JsonValueKind.String ? audience.GetString() == clientId : audience.ValueKind == JsonValueKind.Array && audience.EnumerateArray().Any(value => value.GetString() == clientId);
            if (claims.GetProperty("iss").GetString() != Issuer || !audienceMatches || claims.GetProperty("exp").GetInt64() <= now.ToUnixTimeSeconds() ||
                claims.TryGetProperty("nbf", out var notBefore) && notBefore.GetInt64() > now.AddSeconds(30).ToUnixTimeSeconds() ||
                claims.TryGetProperty("iat", out var issuedAt) && issuedAt.GetInt64() > now.AddSeconds(30).ToUnixTimeSeconds() ||
                claims.TryGetProperty("azp", out var authorizedParty) && authorizedParty.GetString() != clientId ||
                audience.ValueKind == JsonValueKind.Array && audience.GetArrayLength() > 1 && !claims.TryGetProperty("azp", out _)) throw new InvalidOperationException();
            if (nonce is not null && !OpenRouterOAuthService.SecureEquals(nonce, claims.GetProperty("nonce").GetString() ?? "")) throw new InvalidOperationException();
            var subject = claims.GetProperty("sub").GetString();
            if (string.IsNullOrEmpty(subject) || subject.Length > 512) throw new InvalidOperationException();
            var email = claims.TryGetProperty("email", out var emailValue) && emailValue.ValueKind == JsonValueKind.String ? emailValue.GetString() : null;
            return new(subject, email is { Length: <= 320 } ? email : null);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or CryptographicException or ArgumentException)
        {
            throw new InvalidOperationException("OpenAI ID-token signature, identity, audience, expiry or nonce validation failed.");
        }
    }

    internal static bool PlanEnabled(IEnumerable<string> scopes) => scopes.Contains("chatgpt.tokens.use.direct", StringComparer.Ordinal) && scopes.Contains("resource.invoke", StringComparer.Ordinal);
    internal static bool IsTerminalRefreshError(string code) => code is "invalid_grant" or "invalid_refresh_token" or "token_expired" or "refresh_token_expired" or "refresh_token_invalidated" or "refresh_token_reused";
    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
    private static Account Find(Store store, string? id) => store.Accounts.SingleOrDefault(account => account.Id == id) ?? throw new InvalidOperationException("Select a saved ChatGPT account or sign in to add one.");
    private static void ClearTokens(Account account) { account.AccessToken = null; account.RefreshToken = null; account.IdToken = null; account.Scopes = []; }

    private static void ApplyTokens(Account account, JsonElement tokens, string? email, bool refresh, DateTimeOffset receivedAt)
    {
        var access = tokens.GetProperty("access_token").GetString();
        var renewable = tokens.GetProperty("refresh_token").GetString();
        var lifetime = tokens.GetProperty("expires_in").GetInt32();
        static bool ValidToken(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 32768 && value.All(character => character is > ' ' and < '\u007f');
        if (tokens.GetProperty("token_type").GetString()?.Equals("Bearer", StringComparison.OrdinalIgnoreCase) != true || !ValidToken(access) || !ValidToken(renewable) || lifetime is <= 0 or > 86400)
            throw new InvalidOperationException("OpenAI returned an incomplete credential set.");
        account.AccessToken = access; account.RefreshToken = renewable; account.Email = email;
        account.ExpiresAt = receivedAt.AddSeconds(lifetime);
        account.EarliestRefreshAt = tokens.TryGetProperty("earliest_refresh_at", out var earliest) && earliest.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(earliest.GetInt64()) : DateTimeOffset.MinValue;
        if (tokens.TryGetProperty("id_token", out var id)) account.IdToken = id.GetString();
        account.Scopes = tokens.TryGetProperty("scope", out var scope) ? (scope.GetString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries) : refresh ? account.Scopes : [];
    }

    private static async Task<JsonDocument> ExchangeAsync(Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        using var response = await Http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(fields), cancellationToken);
        using var body = JsonDocument.Parse(await LlmClient.ReadLimitedAsync(response, cancellationToken));
        if (!response.IsSuccessStatusCode)
        {
            var code = body.RootElement.TryGetProperty("error", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (code is { Length: > 128 } || code?.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-')) == true) code = null;
            var error = new InvalidOperationException($"OpenAI token exchange failed (HTTP {(int)response.StatusCode}, {code ?? "unknown_error"}).");
            if (code is not null) error.Data["oauthCode"] = code;
            throw error;
        }
        return JsonDocument.Parse(body.RootElement.GetRawText());
    }
    private static async Task<JsonDocument> ReadJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"OpenAI identity metadata unavailable (HTTP {(int)response.StatusCode}).");
        return JsonDocument.Parse(await LlmClient.ReadLimitedAsync(response, cancellationToken));
    }
    private static Store Load()
    {
        if (!File.Exists(StorePath)) return new();
        using var input = new FileStream(StorePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (input.Length > 16 * 1024 * 1024) throw new InvalidOperationException("ChatGPT credential storage exceeded its size limit.");
        using var bytes = new MemoryStream(); input.CopyTo(bytes);
        return JsonSerializer.Deserialize<Store>(ProtectedData.Unprotect(bytes.ToArray(), null, DataProtectionScope.CurrentUser))
            ?? throw new InvalidOperationException("ChatGPT credential storage is invalid.");
    }
    private static void Save(Store store)
    {
        Directory.CreateDirectory(SettingsService.DataDirectory);
        var temporary = StorePath + ".tmp";
        File.WriteAllBytes(temporary, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(store), null, DataProtectionScope.CurrentUser));
        File.Move(temporary, StorePath, true);
    }
    private static async Task<FileStream> LockAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(SettingsService.DataDirectory);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(StorePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline) { await Task.Delay(200, cancellationToken); }
        }
    }
    private sealed class Store { public string HostId { get; set; } = ""; public List<Account> Accounts { get; set; } = []; }
    private sealed class Account
    {
        public string Id { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string Subject { get; set; } = "";
        public string? Email { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? IdToken { get; set; }
        public string[] Scopes { get; set; } = [];
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset EarliestRefreshAt { get; set; }
    }
}
