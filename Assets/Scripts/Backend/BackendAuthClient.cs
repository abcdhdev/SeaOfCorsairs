using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public sealed class BackendAuthClient
{
    private readonly string _authBaseUrl;

    public BackendAuthClient(string authBaseUrl)
    {
        _authBaseUrl = NormalizeBaseUrl(authBaseUrl);
        if (string.IsNullOrWhiteSpace(_authBaseUrl))
        {
            throw new ArgumentException("Auth base URL is required.", nameof(authBaseUrl));
        }
    }

    public async Task<TokenResponse> RegisterAsync(string email, string password, string displayName, CancellationToken cancellationToken = default)
    {
        var url = $"{_authBaseUrl}/v1/auth/register";
        var body = JsonUtility.ToJson(new RegisterRequest
        {
            email = (email ?? string.Empty).Trim(),
            password = password ?? string.Empty,
            displayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
        });

        return await PostJsonAsync<TokenResponse>(url, body, cancellationToken);
    }

    public async Task<TokenResponse> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var url = $"{_authBaseUrl}/v1/auth/login";
        var body = JsonUtility.ToJson(new LoginRequest
        {
            email = (email ?? string.Empty).Trim(),
            password = password ?? string.Empty,
        });

        return await PostJsonAsync<TokenResponse>(url, body, cancellationToken);
    }

    public async Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var url = $"{_authBaseUrl}/v1/auth/refresh";
        var body = JsonUtility.ToJson(new RefreshRequest
        {
            refreshToken = refreshToken ?? string.Empty,
        });

        return await PostJsonAsync<TokenResponse>(url, body, cancellationToken);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var url = $"{_authBaseUrl}/v1/auth/logout";
        var body = JsonUtility.ToJson(new LogoutRequest
        {
            refreshToken = refreshToken ?? string.Empty,
        });

        await PostJsonAsync<object>(url, body, cancellationToken);
    }

    public async Task<MeResponse> MeAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var url = $"{_authBaseUrl}/v1/auth/me";
        using var request = UnityWebRequest.Get(url);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Authorization", $"Bearer {accessToken ?? string.Empty}");

        await request.SendWebRequestAsync(cancellationToken);
        return ParseOrThrow<MeResponse>(request, url);
    }

    private static async Task<T> PostJsonAsync<T>(string url, string bodyJson, CancellationToken cancellationToken)
    {
        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        var bytes = Encoding.UTF8.GetBytes(bodyJson ?? "{}");
        request.uploadHandler = new UploadHandlerRaw(bytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        await request.SendWebRequestAsync(cancellationToken);
        return ParseOrThrow<T>(request, url);
    }

    private static T ParseOrThrow<T>(UnityWebRequest request, string url)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var statusCode = request.responseCode;
        var text = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;

        // Network/HTTP errors (includes non-2xx as ProtocolError in modern Unity).
        if (request.result != UnityWebRequest.Result.Success)
        {
            throw BackendApiException.FromResponse(url, statusCode, request.error, text);
        }

        if (statusCode < 200 || statusCode >= 300)
        {
            throw BackendApiException.FromResponse(url, statusCode, request.error, text);
        }

        if (typeof(T) == typeof(object))
        {
            return default;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw BackendApiException.FromResponse(url, statusCode, "Empty response", text);
        }

        try
        {
            return JsonUtility.FromJson<T>(text);
        }
        catch (Exception ex)
        {
            throw BackendApiException.FromResponse(url, statusCode, $"Failed to parse JSON: {ex.Message}", text);
        }
    }

    private static string NormalizeBaseUrl(string url)
    {
        url ??= string.Empty;
        url = url.Trim();
        while (url.EndsWith("/", StringComparison.Ordinal))
        {
            url = url.Substring(0, url.Length - 1);
        }

        return url;
    }

    [Serializable]
    private sealed class RegisterRequest
    {
        public string email;
        public string password;
        public string displayName;
    }

    [Serializable]
    private sealed class LoginRequest
    {
        public string email;
        public string password;
    }

    [Serializable]
    private sealed class RefreshRequest
    {
        public string refreshToken;
    }

    [Serializable]
    private sealed class LogoutRequest
    {
        public string refreshToken;
    }
}

[Serializable]
public sealed class TokenResponse
{
    public string accessToken;
    public string refreshToken;
    public int expiresInSeconds;
}

[Serializable]
public sealed class MeResponse
{
    public string userId;
    public string email;
    public string displayName;
}

[Serializable]
public sealed class BackendErrorResponse
{
    public string code;
    public string message;
}

public sealed class BackendApiException : Exception
{
    public string Url { get; }
    public long StatusCode { get; }
    public string BackendCode { get; }

    public BackendApiException(string message, string url, long statusCode, string backendCode, Exception inner = null)
        : base(message, inner)
    {
        Url = url;
        StatusCode = statusCode;
        BackendCode = backendCode ?? string.Empty;
    }

    public static BackendApiException FromResponse(string url, long statusCode, string unityError, string responseText)
    {
        string backendCode = string.Empty;
        string backendMessage = string.Empty;

        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                var parsed = JsonUtility.FromJson<BackendErrorResponse>(responseText);
                if (parsed != null)
                {
                    backendCode = parsed.code ?? string.Empty;
                    backendMessage = parsed.message ?? string.Empty;
                }
            }
            catch
            {
                // Ignore parse failures; fallback to raw text/unity error.
            }
        }

        var msg = !string.IsNullOrWhiteSpace(backendMessage)
            ? backendMessage
            : (!string.IsNullOrWhiteSpace(unityError) ? unityError : "Request failed.");

        // Include HTTP status for easier debugging.
        msg = $"{msg} (HTTP {statusCode})";

        if (!string.IsNullOrWhiteSpace(backendCode))
        {
            msg = $"{msg} [{backendCode}]";
        }

        return new BackendApiException(msg, url, statusCode, backendCode);
    }
}

