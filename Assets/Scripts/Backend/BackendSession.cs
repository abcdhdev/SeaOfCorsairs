using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SavedAccount
{
    public string Email;
    public string Password;
}

[Serializable]
public class SavedAccountsList
{
    public List<SavedAccount> Accounts = new List<SavedAccount>();
}

public static class BackendSession
{
    // Defaults match docker-compose local ports from this repo's backend stack.
    public const string DefaultAuthBaseUrl = "http://localhost:8081";
    public const string DefaultPlayerDataBaseUrl = "http://localhost:8082";

    private const string PrefKeyAuthBaseUrl = "seawars.backend.authBaseUrl";
    private const string PrefKeyPlayerDataBaseUrl = "seawars.backend.playerDataBaseUrl";
    private const string PrefKeyAccessToken = "seawars.auth.accessToken";
    private const string PrefKeyRefreshToken = "seawars.auth.refreshToken";
    private const string PrefKeyAccessTokenExpiresAtUtc = "seawars.auth.accessTokenExpiresAtUtc";
    private const string PrefKeyLastEmail = "seawars.auth.lastEmail";
    private const string PrefKeySavedAccounts = "seawars.auth.savedAccounts";

    public static string AuthBaseUrl { get; private set; } = DefaultAuthBaseUrl;
    public static string PlayerDataBaseUrl { get; private set; } = DefaultPlayerDataBaseUrl;

    public static string AccessToken { get; private set; } = string.Empty;
    public static string RefreshToken { get; private set; } = string.Empty;
    public static DateTime AccessTokenExpiresAtUtc { get; private set; } = DateTime.MinValue;
    public static string LastEmail { get; private set; } = string.Empty;

    public static bool HasRefreshToken => !string.IsNullOrWhiteSpace(RefreshToken);
    public static bool HasAccessToken => !string.IsNullOrWhiteSpace(AccessToken);
    public static bool IsLoggedIn => HasAccessToken && HasRefreshToken;

    public static void LoadFromPlayerPrefs()
    {
        AuthBaseUrl = NormalizeBaseUrl(PlayerPrefs.GetString(PrefKeyAuthBaseUrl, DefaultAuthBaseUrl));
        PlayerDataBaseUrl = NormalizeBaseUrl(PlayerPrefs.GetString(PrefKeyPlayerDataBaseUrl, DefaultPlayerDataBaseUrl));

        AccessToken = PlayerPrefs.GetString(PrefKeyAccessToken, string.Empty);
        RefreshToken = PlayerPrefs.GetString(PrefKeyRefreshToken, string.Empty);

        LastEmail = PlayerPrefs.GetString(PrefKeyLastEmail, string.Empty);

        // Stored as ISO 8601.
        var rawExpiresAt = PlayerPrefs.GetString(PrefKeyAccessTokenExpiresAtUtc, string.Empty);
        if (!string.IsNullOrWhiteSpace(rawExpiresAt) && DateTime.TryParse(rawExpiresAt, out var expiresAt))
        {
            AccessTokenExpiresAtUtc = DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc);
        }
        else
        {
            AccessTokenExpiresAtUtc = DateTime.MinValue;
        }
    }

    public static void SaveBaseUrls(string authBaseUrl, string playerDataBaseUrl)
    {
        AuthBaseUrl = NormalizeBaseUrl(string.IsNullOrWhiteSpace(authBaseUrl) ? DefaultAuthBaseUrl : authBaseUrl);
        PlayerDataBaseUrl = NormalizeBaseUrl(string.IsNullOrWhiteSpace(playerDataBaseUrl) ? DefaultPlayerDataBaseUrl : playerDataBaseUrl);

        PlayerPrefs.SetString(PrefKeyAuthBaseUrl, AuthBaseUrl);
        PlayerPrefs.SetString(PrefKeyPlayerDataBaseUrl, PlayerDataBaseUrl);
        PlayerPrefs.Save();
    }

    public static List<SavedAccount> GetSavedAccounts()
    {
        var json = PlayerPrefs.GetString(PrefKeySavedAccounts, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<SavedAccount>();
        }
        try
        {
            var list = JsonUtility.FromJson<SavedAccountsList>(json);
            return list?.Accounts ?? new List<SavedAccount>();
        }
        catch
        {
            return new List<SavedAccount>();
        }
    }

    public static void SaveAccount(string email, string password)
    {
        var accounts = GetSavedAccounts();
        var existing = accounts.Find(a => string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Password = password;
        }
        else
        {
            accounts.Add(new SavedAccount { Email = email, Password = password });
        }

        var list = new SavedAccountsList { Accounts = accounts };
        PlayerPrefs.SetString(PrefKeySavedAccounts, JsonUtility.ToJson(list));
        PlayerPrefs.Save();
    }

    public static void SaveLastEmail(string email)
    {
        LastEmail = (email ?? string.Empty).Trim();
        PlayerPrefs.SetString(PrefKeyLastEmail, LastEmail);
        PlayerPrefs.Save();
    }

    public static void SetTokens(string accessToken, string refreshToken, int expiresInSeconds)
    {
        AccessToken = accessToken ?? string.Empty;
        RefreshToken = refreshToken ?? string.Empty;

        // Subtract a little skew so we refresh slightly early.
        var safeSeconds = Mathf.Max(1, expiresInSeconds - 15);
        AccessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(safeSeconds);

        PlayerPrefs.SetString(PrefKeyAccessToken, AccessToken);
        PlayerPrefs.SetString(PrefKeyRefreshToken, RefreshToken);
        PlayerPrefs.SetString(PrefKeyAccessTokenExpiresAtUtc, AccessTokenExpiresAtUtc.ToString("O"));
        PlayerPrefs.Save();
    }

    public static void ClearTokens()
    {
        AccessToken = string.Empty;
        RefreshToken = string.Empty;
        AccessTokenExpiresAtUtc = DateTime.MinValue;

        PlayerPrefs.DeleteKey(PrefKeyAccessToken);
        PlayerPrefs.DeleteKey(PrefKeyRefreshToken);
        PlayerPrefs.DeleteKey(PrefKeyAccessTokenExpiresAtUtc);
        PlayerPrefs.Save();
    }

    public static bool IsAccessTokenExpiredOrMissing()
    {
        if (!HasAccessToken)
        {
            return true;
        }

        if (AccessTokenExpiresAtUtc == DateTime.MinValue)
        {
            return true;
        }

        return DateTime.UtcNow >= AccessTokenExpiresAtUtc;
    }

    public static string GetAuthBaseUrlOrDefault(string candidate) =>
        NormalizeBaseUrl(string.IsNullOrWhiteSpace(candidate) ? DefaultAuthBaseUrl : candidate);

    public static string GetPlayerDataBaseUrlOrDefault(string candidate) =>
        NormalizeBaseUrl(string.IsNullOrWhiteSpace(candidate) ? DefaultPlayerDataBaseUrl : candidate);

    private static string NormalizeBaseUrl(string url)
    {
        url ??= string.Empty;
        url = url.Trim();
        while (url.EndsWith("/", StringComparison.Ordinal))
        {
            url = url.Substring(0, url.Length - 1);
        }

        return string.IsNullOrWhiteSpace(url) ? string.Empty : url;
    }
}

