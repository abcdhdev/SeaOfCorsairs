using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ChatService : MonoBehaviour
{
    public enum ChatChannel : byte
    {
        Global = 0,
        Local = 1,
        Party = 2,
        Guild = 3,
        System = 4,
    }

    public readonly struct ChatMessage
    {
        public readonly DateTime TimestampUtc;
        public readonly ChatChannel Channel;
        public readonly ulong SenderClientId;
        public readonly string SenderName;
        public readonly string Text;
        public readonly bool IsSystem;

        public ChatMessage(DateTime timestampUtc, ChatChannel channel, ulong senderClientId, string senderName, string text, bool isSystem)
        {
            TimestampUtc = timestampUtc;
            Channel = channel;
            SenderClientId = senderClientId;
            SenderName = senderName ?? "Unknown";
            Text = text ?? string.Empty;
            IsSystem = isSystem;
        }
    }

    public static ChatService Instance { get; private set; }

    public static event Action<ChatMessage> MessageReceived;

    private const string ClientToServerMessageName = "mmo-chat-c2s";
    private const string ServerToClientMessageName = "mmo-chat-s2c";
    private const int MaxChatLength = 220;
    private const float MinSecondsBetweenMessages = 0.35f;
    private const int MaxHistory = 250;

    private readonly List<ChatMessage> _history = new List<ChatMessage>(MaxHistory);
    private readonly Dictionary<ulong, float> _lastMessageTimeByClientId = new Dictionary<ulong, float>(16);

    private NetworkManager _networkManager;
    private bool _registeredHandlers;
    private bool _wasConnected;

    public IReadOnlyList<ChatMessage> History => _history;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
        {
            return;
        }

        var go = new GameObject("ChatService");
        DontDestroyOnLoad(go);
        go.AddComponent<ChatService>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        UnregisterHandlers();
    }

    private void Update()
    {
        var activeManager = NetworkManager.Singleton;
        if (!ReferenceEquals(_networkManager, activeManager))
        {
            UnregisterHandlers();
            _networkManager = activeManager;
        }

        if (_networkManager != null)
        {
            if (_networkManager.IsListening)
            {
                // NetworkManager instance can exist before transport starts.
                // Keep retrying until CustomMessagingManager is ready.
                RegisterHandlers();
            }
            else
            {
                UnregisterHandlers();
            }
        }

        bool isConnectedNow = _networkManager != null && (_networkManager.IsClient || _networkManager.IsServer);
        if (isConnectedNow && !_wasConnected)
        {
            AddSystemMessage("Chat connected.");
        }
        else if (!isConnectedNow && _wasConnected)
        {
            AddSystemMessage("Chat disconnected.");
        }

        _wasConnected = isConnectedNow;
    }

    public bool Send(ChatChannel channel, string text)
    {
        string normalized = NormalizeChatText(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (_networkManager == null || !_networkManager.IsListening)
        {
            AddLocalMessage(new ChatMessage(DateTime.UtcNow, channel, 0, "You", normalized, false));
            return true;
        }

        if (_networkManager.IsServer)
        {
            ulong senderClientId = _networkManager.IsClient ? _networkManager.LocalClientId : 0UL;
            RelayMessageFromServer(channel, senderClientId, normalized);
            return true;
        }

        if (!_networkManager.IsClient)
        {
            AddLocalMessage(new ChatMessage(DateTime.UtcNow, channel, 0, "You", normalized, false));
            return true;
        }

        var messaging = _networkManager.CustomMessagingManager;
        if (messaging == null)
        {
            AddSystemMessage("Chat messaging is unavailable.");
            return false;
        }

        int writeSize = sizeof(byte) + FastBufferWriter.GetWriteSize(normalized);
        using var writer = new FastBufferWriter(writeSize, Allocator.Temp);
        writer.WriteValueSafe((byte)channel);
        writer.WriteValueSafe(normalized);
        messaging.SendNamedMessage(ClientToServerMessageName, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        return true;
    }

    private void RegisterHandlers()
    {
        if (_registeredHandlers || _networkManager == null)
        {
            return;
        }

        var messaging = _networkManager.CustomMessagingManager;
        if (messaging == null)
        {
            return;
        }

        messaging.RegisterNamedMessageHandler(ClientToServerMessageName, OnClientToServerMessage);
        messaging.RegisterNamedMessageHandler(ServerToClientMessageName, OnServerToClientMessage);
        _networkManager.OnClientConnectedCallback += OnClientConnected;
        _networkManager.OnClientDisconnectCallback += OnClientDisconnected;
        _registeredHandlers = true;
    }

    private void UnregisterHandlers()
    {
        if (!_registeredHandlers || _networkManager == null)
        {
            _registeredHandlers = false;
            return;
        }

        var messaging = _networkManager.CustomMessagingManager;
        if (messaging != null)
        {
            messaging.UnregisterNamedMessageHandler(ClientToServerMessageName);
            messaging.UnregisterNamedMessageHandler(ServerToClientMessageName);
        }

        _networkManager.OnClientConnectedCallback -= OnClientConnected;
        _networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        _registeredHandlers = false;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (_networkManager == null || !_networkManager.IsServer)
        {
            return;
        }

        string playerName = ResolveClientDisplayName(clientId);
        AddSystemMessage($"{playerName} joined the session.");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (_networkManager == null || !_networkManager.IsServer)
        {
            return;
        }

        string playerName = ResolveClientDisplayName(clientId);
        AddSystemMessage($"{playerName} left the session.");
        _lastMessageTimeByClientId.Remove(clientId);
    }

    private void OnClientToServerMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (_networkManager == null || !_networkManager.IsServer)
        {
            return;
        }

        reader.ReadValueSafe(out byte channelByte);
        reader.ReadValueSafe(out string rawText);

        ChatChannel channel = ValidateChannel((ChatChannel)channelByte);
        string text = NormalizeChatText(rawText);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        float now = Time.unscaledTime;
        if (_lastMessageTimeByClientId.TryGetValue(senderClientId, out float previousTime))
        {
            if (now - previousTime < MinSecondsBetweenMessages)
            {
                SendSystemMessageToClient(senderClientId, "You are sending too quickly.");
                return;
            }
        }

        _lastMessageTimeByClientId[senderClientId] = now;
        RelayMessageFromServer(channel, senderClientId, text);
    }

    private void OnServerToClientMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (_networkManager == null || !_networkManager.IsClient)
        {
            return;
        }

        reader.ReadValueSafe(out byte channelByte);
        reader.ReadValueSafe(out ulong sourceClientId);
        reader.ReadValueSafe(out bool isSystem);
        reader.ReadValueSafe(out string senderName);
        reader.ReadValueSafe(out string text);

        var message = new ChatMessage(
            DateTime.UtcNow,
            ValidateChannel((ChatChannel)channelByte),
            sourceClientId,
            senderName,
            NormalizeChatText(text),
            isSystem);

        AddLocalMessage(message);
    }

    private void RelayMessageFromServer(ChatChannel channel, ulong senderClientId, string text)
    {
        if (_networkManager == null || !_networkManager.IsServer)
        {
            return;
        }

        bool isSystem = channel == ChatChannel.System;
        string senderName = isSystem ? "System" : ResolveClientDisplayName(senderClientId);
        string normalizedText = NormalizeChatText(text);

        var message = new ChatMessage(DateTime.UtcNow, channel, senderClientId, senderName, normalizedText, isSystem);
        AddLocalMessage(message);

        var messaging = _networkManager.CustomMessagingManager;
        if (messaging == null)
        {
            return;
        }

        foreach (var client in _networkManager.ConnectedClientsList)
        {
            if (client == null)
            {
                continue;
            }

            if (_networkManager.IsHost && client.ClientId == _networkManager.LocalClientId)
            {
                continue;
            }

            SendSerializedServerMessage(messaging, client.ClientId, message);
        }
    }

    private void AddSystemMessage(string text)
    {
        text = NormalizeChatText(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (_networkManager != null && _networkManager.IsServer && _networkManager.IsListening)
        {
            RelayMessageFromServer(ChatChannel.System, 0UL, text);
            return;
        }

        AddLocalMessage(new ChatMessage(DateTime.UtcNow, ChatChannel.System, 0UL, "System", text, true));
    }

    private void SendSystemMessageToClient(ulong clientId, string text)
    {
        if (_networkManager == null || !_networkManager.IsServer)
        {
            return;
        }

        string normalized = NormalizeChatText(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var messaging = _networkManager.CustomMessagingManager;
        if (messaging == null)
        {
            return;
        }

        var message = new ChatMessage(DateTime.UtcNow, ChatChannel.System, 0UL, "System", normalized, true);
        SendSerializedServerMessage(messaging, clientId, message);
    }

    private static void SendSerializedServerMessage(CustomMessagingManager messaging, ulong targetClientId, ChatMessage message)
    {
        int writeSize = sizeof(byte) + sizeof(ulong) + sizeof(bool);
        writeSize += FastBufferWriter.GetWriteSize(message.SenderName);
        writeSize += FastBufferWriter.GetWriteSize(message.Text);

        using var writer = new FastBufferWriter(writeSize, Allocator.Temp);
        writer.WriteValueSafe((byte)message.Channel);
        writer.WriteValueSafe(message.SenderClientId);
        writer.WriteValueSafe(message.IsSystem);
        writer.WriteValueSafe(message.SenderName);
        writer.WriteValueSafe(message.Text);
        messaging.SendNamedMessage(ServerToClientMessageName, targetClientId, writer, NetworkDelivery.ReliableSequenced);
    }

    private static ChatChannel ValidateChannel(ChatChannel channel)
    {
        if (channel == ChatChannel.Global || channel == ChatChannel.Local || channel == ChatChannel.Party || channel == ChatChannel.Guild || channel == ChatChannel.System)
        {
            return channel;
        }

        return ChatChannel.Global;
    }

    private static string NormalizeChatText(string text)
    {
        text = UiTextSanitizer.SanitizeForLabel(text, collapseWhitespace: true);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (text.Length > MaxChatLength)
        {
            text = text.Substring(0, MaxChatLength);
        }

        return text;
    }

    private string ResolveClientDisplayName(ulong clientId)
    {
        if (PlayerManager.Instance != null)
        {
            Player player = PlayerManager.Instance.GetPlayer(clientId);
            if (player != null)
            {
                string syncName = player.PlayerName.Value.ToString();
                if (!string.IsNullOrWhiteSpace(syncName))
                {
                    return UiTextSanitizer.SanitizeForLabel(syncName, collapseWhitespace: true);
                }

                if (player.gameObject != null)
                {
                    return UiTextSanitizer.SanitizeForLabel(TrimCloneSuffix(player.gameObject.name), collapseWhitespace: true);
                }
            }
        }

        if (_networkManager != null && _networkManager.IsClient && clientId == _networkManager.LocalClientId)
        {
            return "You";
        }

        return $"Captain {clientId}";
    }

    private static string TrimCloneSuffix(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        const string cloneSuffix = "(Clone)";
        if (value.EndsWith(cloneSuffix, StringComparison.Ordinal))
        {
            value = value.Substring(0, value.Length - cloneSuffix.Length).TrimEnd();
        }

        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
    }

    private void AddLocalMessage(ChatMessage message)
    {
        if (_history.Count >= MaxHistory)
        {
            _history.RemoveAt(0);
        }

        _history.Add(message);
        MessageReceived?.Invoke(message);
    }
}
