using System;
using System.Text;
using UnityEngine;

/// <summary>
/// Versioned wrapper for Netcode ConnectionData.
/// Carries auth token plus compatibility metadata so the server can reject outdated clients.
/// </summary>
public static class NetcodeConnectionPayload
{
    private const string CliClientVersionEnvVar = "SEAWARS_CLIENT_VERSION";
    private const string CliProtocolVersionEnvVar = "SEAWARS_PROTOCOL_VERSION";
    private const string CompactPayloadPrefix = "sw1";

    public const int CurrentSchemaVersion = 1;
    public const int CurrentProtocolVersion = 2;

    [Serializable]
    private sealed class PayloadEnvelope
    {
        public int schemaVersion = CurrentSchemaVersion;
        public int protocolVersion = CurrentProtocolVersion;
        public string clientVersion = "";
        public string accessToken = "";
        public string refreshToken = "";
    }

    public struct ParsedPayload
    {
        public string AccessToken;
        public string RefreshToken;
        public string ClientVersion;
        public int ProtocolVersion;
    }

    public static byte[] Build(string accessToken, string refreshToken = null)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Array.Empty<byte>();
        }

        // Compact wire format to keep payload small:
        // sw1|<protocolVersion>|<clientVersion>|<jwt>|<refreshToken?>
        // The previous JSON envelope is still accepted by TryParse for compatibility.
        string compact = string.IsNullOrWhiteSpace(refreshToken)
            ? $"{CompactPayloadPrefix}|{ResolveClientProtocolVersion()}|{ResolveClientVersion()}|{accessToken.Trim()}"
            : $"{CompactPayloadPrefix}|{ResolveClientProtocolVersion()}|{ResolveClientVersion()}|{accessToken.Trim()}|{refreshToken.Trim()}";
        return Encoding.UTF8.GetBytes(compact);
    }

    public static bool TryParse(byte[] payloadBytes, out ParsedPayload payload, out string rejectReason)
    {
        payload = new ParsedPayload();
        rejectReason = null;

        if (payloadBytes == null || payloadBytes.Length == 0)
        {
            rejectReason = "No authentication token provided. Sign in before starting the Netcode client.";
            return false;
        }

        string rawPayload;
        try
        {
            rawPayload = Encoding.UTF8.GetString(payloadBytes);
        }
        catch (Exception ex)
        {
            rejectReason = $"Invalid connection payload encoding: {ex.Message}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            rejectReason = "Empty authentication token.";
            return false;
        }

        rawPayload = rawPayload.Trim();
        if (rawPayload.StartsWith($"{CompactPayloadPrefix}|", StringComparison.Ordinal))
        {
            return TryParseCompactEnvelope(rawPayload, out payload, out rejectReason);
        }

        if (rawPayload.StartsWith("{", StringComparison.Ordinal))
        {
            return TryParseJsonEnvelope(rawPayload, out payload, out rejectReason);
        }

        if (LooksLikeJwt(rawPayload))
        {
            rejectReason = "Client update required. This server requires a versioned handshake payload.";
            return false;
        }

        rejectReason = "Unsupported connection payload format.";
        return false;
    }

    private static bool TryParseCompactEnvelope(string rawPayload, out ParsedPayload payload, out string rejectReason)
    {
        payload = new ParsedPayload();
        rejectReason = null;

        string[] parts = rawPayload.Split('|');
        if (parts.Length < 4 || parts.Length > 5)
        {
            rejectReason = "Malformed connection payload.";
            return false;
        }

        string prefix = parts[0];
        if (!string.Equals(prefix, CompactPayloadPrefix, StringComparison.Ordinal))
        {
            rejectReason = "Unsupported connection payload format.";
            return false;
        }

        string protocolPart = parts[1];
        if (!int.TryParse(protocolPart, out int protocolVersion) || protocolVersion <= 0)
        {
            rejectReason = "Connection payload missing protocol version.";
            return false;
        }

        string clientVersion = parts[2].Trim();
        if (string.IsNullOrWhiteSpace(clientVersion))
        {
            rejectReason = "Connection payload missing client version.";
            return false;
        }

        string accessToken = parts[3].Trim();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            rejectReason = "Connection payload missing access token.";
            return false;
        }

        if (!LooksLikeJwt(accessToken))
        {
            rejectReason = "Connection payload is corrupted (access token is incomplete).";
            return false;
        }

        payload = new ParsedPayload
        {
            AccessToken = accessToken,
            RefreshToken = parts.Length >= 5 ? parts[4].Trim() : string.Empty,
            ClientVersion = clientVersion,
            ProtocolVersion = protocolVersion,
        };

        return true;
    }

    private static bool TryParseJsonEnvelope(string rawPayload, out ParsedPayload payload, out string rejectReason)
    {
        payload = new ParsedPayload();
        rejectReason = null;

        PayloadEnvelope envelope;
        try
        {
            envelope = JsonUtility.FromJson<PayloadEnvelope>(rawPayload);
        }
        catch (Exception ex)
        {
            rejectReason = $"Malformed connection payload JSON: {ex.Message}";
            return false;
        }

        if (envelope == null)
        {
            rejectReason = "Malformed connection payload.";
            return false;
        }

        if (envelope.schemaVersion != CurrentSchemaVersion)
        {
            rejectReason = $"Handshake schema mismatch. Server expects {CurrentSchemaVersion}, client sent {envelope.schemaVersion}.";
            return false;
        }

        if (envelope.protocolVersion <= 0)
        {
            rejectReason = "Connection payload missing protocol version.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(envelope.clientVersion))
        {
            rejectReason = "Connection payload missing client version.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(envelope.accessToken))
        {
            rejectReason = "Connection payload missing access token.";
            return false;
        }

        if (!LooksLikeJwt(envelope.accessToken))
        {
            rejectReason = "Connection payload is corrupted (access token is incomplete).";
            return false;
        }

        payload = new ParsedPayload
        {
            AccessToken = envelope.accessToken.Trim(),
            RefreshToken = string.IsNullOrWhiteSpace(envelope.refreshToken) ? string.Empty : envelope.refreshToken.Trim(),
            ClientVersion = envelope.clientVersion.Trim(),
            ProtocolVersion = envelope.protocolVersion,
        };

        return true;
    }

    private static bool LooksLikeJwt(string payload)
    {
        int firstDot = payload.IndexOf('.');
        if (firstDot <= 0)
        {
            return false;
        }

        int secondDot = payload.IndexOf('.', firstDot + 1);
        return secondDot > firstDot + 1 && secondDot < payload.Length - 1;
    }

    private static string ResolveClientVersion()
    {
        string fromEnv = Environment.GetEnvironmentVariable(CliClientVersionEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Trim();
        }

        if (!string.IsNullOrWhiteSpace(Application.version))
        {
            return Application.version.Trim();
        }

        return "unknown";
    }

    private static int ResolveClientProtocolVersion()
    {
        string fromEnv = Environment.GetEnvironmentVariable(CliProtocolVersionEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv) &&
            int.TryParse(fromEnv.Trim(), out int parsed) &&
            parsed > 0)
        {
            return parsed;
        }

        return CurrentProtocolVersion;
    }
}
