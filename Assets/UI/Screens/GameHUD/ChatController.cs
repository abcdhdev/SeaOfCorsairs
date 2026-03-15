using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
public sealed class ChatController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;

    private const string ChatRootName = "MmoChatRoot";
    private const string ChatHeaderTitleName = "ChatHeaderTitle";
    private const string ChatHeaderName = "ChatHeader";
    private const string ChatUnreadBadgeName = "ChatUnreadBadge";
    private const string ChatToggleButtonName = "ChatToggleButton";
    private const string ChatBodyName = "ChatBody";
    private const string ChatMessagesScrollName = "ChatMessagesScroll";
    private const string ChatMessagesContentName = "ChatMessagesContent";
    private const string ChatInputName = "ChatInputField";
    private const string ChatSendButtonName = "ChatSendButton";
    private const string ChatChannelGlobalName = "ChatChannelGlobal";
    private const string ChatChannelLocalName = "ChatChannelLocal";
    private const string ChatChannelPartyName = "ChatChannelParty";
    private const string ChatChannelGuildName = "ChatChannelGuild";
    private const string ChatClosedClass = "mmo-chat-closed";
    private const string ChatChannelSelectedClass = "mmo-chat-channel-selected";
    private const int MaxRenderedRows = 160;
    private static ChatController _activeInstance;

    private UIDocument _resolvedDocument;
    private VisualElement _chatRoot;
    private VisualElement _chatHeader;
    private Label _chatHeaderTitle;
    private Label _chatUnreadBadge;
    private Button _chatToggleButton;
    private VisualElement _chatBody;
    private ScrollView _messagesScroll;
    private VisualElement _messagesContent;
    private TextField _inputField;
    private Button _sendButton;
    private readonly Dictionary<ChatService.ChatChannel, Button> _channelButtons = new Dictionary<ChatService.ChatChannel, Button>(4);
    private readonly Dictionary<ChatService.ChatChannel, Action> _channelClickHandlers = new Dictionary<ChatService.ChatChannel, Action>(4);

    private bool _isOpen = true;
    private int _unreadCount;
    private ChatService.ChatChannel _activeChannel = ChatService.ChatChannel.Global;
    private bool _isDraggingChat;
    private int _chatDragPointerId = -1;
    private Vector2 _chatDragPointerOffset;
    private bool _allowNextInputFocus;

    public static bool IsChatInputFocused => _activeInstance != null && _activeInstance.IsInputFocused();

    /// <summary>
    /// Call from external systems (e.g. InputHandler) to blur the chat input
    /// when the player interacts with the game world.
    /// </summary>
    public static void TryBlurChatInput()
    {
        if (_activeInstance != null && _activeInstance.IsInputFocused())
        {
            _activeInstance.BlurInputField();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        ChatController existing = FindFirstObjectByType<ChatController>();
        if (existing != null)
        {
            return;
        }

        var go = new GameObject("ChatController");
        DontDestroyOnLoad(go);
        go.AddComponent<ChatController>();
    }

    private void OnEnable()
    {
        _activeInstance = this;
        TryBindUi();
        ChatService.MessageReceived += OnChatMessageReceived;
    }

    private void OnDisable()
    {
        ChatService.MessageReceived -= OnChatMessageReceived;
        UnregisterUiCallbacks();
        StopChatDrag();
        _allowNextInputFocus = false;
        if (ReferenceEquals(_activeInstance, this))
        {
            _activeInstance = null;
        }

        _chatRoot?.AllowRaycasts();
        _resolvedDocument = null;
        _chatRoot = null;
        _chatHeader = null;
    }

    private void TryBindUi()
    {
        if (_chatRoot != null && _chatRoot.panel != null)
        {
            return;
        }

        _resolvedDocument = uiDocument != null ? uiDocument : FindMainHudDocument();
        if (_resolvedDocument == null || _resolvedDocument.rootVisualElement == null)
        {
            return;
        }

        VisualElement root = _resolvedDocument.rootVisualElement;
        _chatRoot = root.Q<VisualElement>(ChatRootName);
        if (_chatRoot == null)
        {
            return;
        }

        _chatHeaderTitle = _chatRoot.Q<Label>(ChatHeaderTitleName);
        _chatHeader = _chatRoot.Q<VisualElement>(ChatHeaderName);
        _chatUnreadBadge = _chatRoot.Q<Label>(ChatUnreadBadgeName);
        _chatToggleButton = _chatRoot.Q<Button>(ChatToggleButtonName);
        _chatBody = _chatRoot.Q<VisualElement>(ChatBodyName);
        _messagesScroll = _chatRoot.Q<ScrollView>(ChatMessagesScrollName);
        _messagesContent = _chatRoot.Q<VisualElement>(ChatMessagesContentName);
        _inputField = _chatRoot.Q<TextField>(ChatInputName);
        _sendButton = _chatRoot.Q<Button>(ChatSendButtonName);
        if (_chatHeaderTitle != null)
        {
            _chatHeaderTitle.enableRichText = false;
        }

        if (_chatUnreadBadge != null)
        {
            _chatUnreadBadge.enableRichText = false;
        }

        _channelButtons.Clear();
        _channelClickHandlers.Clear();
        RegisterChannelButton(root.Q<Button>(ChatChannelGlobalName), ChatService.ChatChannel.Global);
        RegisterChannelButton(root.Q<Button>(ChatChannelLocalName), ChatService.ChatChannel.Local);
        RegisterChannelButton(root.Q<Button>(ChatChannelPartyName), ChatService.ChatChannel.Party);
        RegisterChannelButton(root.Q<Button>(ChatChannelGuildName), ChatService.ChatChannel.Guild);

        RegisterUiCallbacks();
        RegisterRootPointerDown();
        _chatRoot.BlockRaycasts();
        ApplyOpenClosedVisuals();
        RefreshChannelVisuals();
        RefreshUnreadBadge();
        RebuildFromHistory();
    }

    private void Update()
    {
        if (_chatRoot == null || _chatRoot.panel == null)
        {
            TryBindUi();
        }
    }

    private static UIDocument FindMainHudDocument()
    {
        UIDocument[] docs = FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
        if (docs == null || docs.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < docs.Length; i++)
        {
            UIDocument doc = docs[i];
            if (doc == null || doc.rootVisualElement == null)
            {
                continue;
            }

            if (doc.rootVisualElement.Q<VisualElement>("HudRoot") != null)
            {
                return doc;
            }
        }

        return docs[0];
    }

    private void RegisterChannelButton(Button button, ChatService.ChatChannel channel)
    {
        if (button == null)
        {
            return;
        }

        _channelButtons[channel] = button;
    }

    private void RegisterUiCallbacks()
    {
        if (_chatToggleButton != null)
        {
            _chatToggleButton.clicked -= OnToggleClicked;
            _chatToggleButton.clicked += OnToggleClicked;
        }

        if (_sendButton != null)
        {
            _sendButton.clicked -= SendFromInput;
            _sendButton.clicked += SendFromInput;
        }

        if (_inputField != null)
        {
            _inputField.RegisterCallback<KeyDownEvent>(OnInputFieldKeyDown);
            _inputField.RegisterCallback<PointerDownEvent>(OnInputFieldPointerDown, TrickleDown.TrickleDown);
            _inputField.RegisterCallback<FocusInEvent>(OnInputFieldFocusIn);
        }

        if (_chatRoot != null)
        {
            _chatRoot.RegisterCallback<KeyDownEvent>(OnChatRootKeyDown, TrickleDown.TrickleDown);
        }

        if (_chatHeader != null)
        {
            _chatHeader.RegisterCallback<PointerDownEvent>(OnChatHeaderPointerDown);
            _chatHeader.RegisterCallback<PointerMoveEvent>(OnChatHeaderPointerMove);
            _chatHeader.RegisterCallback<PointerUpEvent>(OnChatHeaderPointerUp);
            _chatHeader.RegisterCallback<PointerCancelEvent>(OnChatHeaderPointerCancel);
        }

        foreach (var pair in _channelButtons)
        {
            var channel = pair.Key;
            var button = pair.Value;
            if (button == null)
            {
                continue;
            }

            if (_channelClickHandlers.TryGetValue(channel, out Action existingHandler))
            {
                button.clicked -= existingHandler;
            }

            Action handler = () => OnChannelSelected(channel);
            _channelClickHandlers[channel] = handler;
            button.clicked += handler;
        }
    }

    private void UnregisterUiCallbacks()
    {
        if (_chatToggleButton != null)
        {
            _chatToggleButton.clicked -= OnToggleClicked;
        }

        if (_sendButton != null)
        {
            _sendButton.clicked -= SendFromInput;
        }

        if (_inputField != null)
        {
            _inputField.UnregisterCallback<KeyDownEvent>(OnInputFieldKeyDown);
            _inputField.UnregisterCallback<PointerDownEvent>(OnInputFieldPointerDown, TrickleDown.TrickleDown);
            _inputField.UnregisterCallback<FocusInEvent>(OnInputFieldFocusIn);
        }

        if (_chatRoot != null)
        {
            _chatRoot.UnregisterCallback<KeyDownEvent>(OnChatRootKeyDown, TrickleDown.TrickleDown);
        }

        if (_chatHeader != null)
        {
            _chatHeader.UnregisterCallback<PointerDownEvent>(OnChatHeaderPointerDown);
            _chatHeader.UnregisterCallback<PointerMoveEvent>(OnChatHeaderPointerMove);
            _chatHeader.UnregisterCallback<PointerUpEvent>(OnChatHeaderPointerUp);
            _chatHeader.UnregisterCallback<PointerCancelEvent>(OnChatHeaderPointerCancel);
        }

        foreach (var pair in _channelButtons)
        {
            if (pair.Value == null)
            {
                continue;
            }

            if (_channelClickHandlers.TryGetValue(pair.Key, out Action handler))
            {
                pair.Value.clicked -= handler;
            }
        }
    }

    private void OnToggleClicked()
    {
        _isOpen = !_isOpen;
        if (_isOpen)
        {
            _unreadCount = 0;
            RefreshUnreadBadge();
        }
        else
        {
            BlurInputField();
        }

        ApplyOpenClosedVisuals();
    }

    private void OnChannelSelected(ChatService.ChatChannel channel)
    {
        if (channel == ChatService.ChatChannel.System)
        {
            return;
        }

        _activeChannel = channel;
        RefreshChannelVisuals();
        UpdateHeaderTitle();
    }

    private void UpdateHeaderTitle()
    {
        if (_chatHeaderTitle == null)
        {
            return;
        }

        _chatHeaderTitle.text = $"Chat [{ChannelToShortTag(_activeChannel)}]";
    }

    private void RefreshChannelVisuals()
    {
        foreach (var pair in _channelButtons)
        {
            if (pair.Value == null)
            {
                continue;
            }

            if (pair.Key == _activeChannel)
            {
                pair.Value.AddToClassList(ChatChannelSelectedClass);
            }
            else
            {
                pair.Value.RemoveFromClassList(ChatChannelSelectedClass);
            }
        }

        UpdateHeaderTitle();
    }

    private void ApplyOpenClosedVisuals()
    {
        if (_chatRoot == null)
        {
            return;
        }

        if (_isOpen)
        {
            _chatRoot.RemoveFromClassList(ChatClosedClass);
            if (_chatToggleButton != null)
            {
                _chatToggleButton.text = "_";
            }
        }
        else
        {
            _chatRoot.AddToClassList(ChatClosedClass);
            if (_chatToggleButton != null)
            {
                _chatToggleButton.text = "+";
            }
        }
    }

    private void OnInputFieldKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Tab)
        {
            CycleChannel();
            evt.StopPropagation();
            evt.PreventDefault();
        }
    }

    private void OnChatRootKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
        {
            if (IsInputFocused())
            {
                SendFromInput();
                evt.StopPropagation();
                evt.PreventDefault();
                return;
            }

            if (!_isOpen)
            {
                _isOpen = true;
                _unreadCount = 0;
                RefreshUnreadBadge();
                ApplyOpenClosedVisuals();
            }

            FocusInputFieldFromUserIntent();
            evt.StopPropagation();
            evt.PreventDefault();

            return;
        }

        if (evt.keyCode == KeyCode.Escape && IsInputFocused())
        {
            BlurInputField();
            evt.StopPropagation();
            evt.PreventDefault();
        }
    }

    private bool IsInputFocused()
    {
        if (_inputField == null || _inputField.panel == null || _inputField.panel.focusController == null)
        {
            return false;
        }

        Focusable focusedElement = _inputField.panel.focusController.focusedElement;
        if (ReferenceEquals(focusedElement, _inputField))
        {
            return true;
        }

        var focusedVisualElement = focusedElement as VisualElement;
        while (focusedVisualElement != null)
        {
            if (ReferenceEquals(focusedVisualElement, _inputField))
            {
                return true;
            }

            focusedVisualElement = focusedVisualElement.parent;
        }

        return false;
    }

    private void OnChatHeaderPointerDown(PointerDownEvent evt)
    {
        if (evt.button != (int)MouseButton.LeftMouse || _chatRoot == null || _chatHeader == null)
        {
            return;
        }

        if (evt.target is Button)
        {
            return;
        }

        Rect chatBounds = _chatRoot.worldBound;
        _chatRoot.style.right = StyleKeyword.Auto;
        _chatRoot.style.bottom = StyleKeyword.Auto;
        _chatRoot.style.left = chatBounds.xMin;
        _chatRoot.style.top = chatBounds.yMin;

        _isDraggingChat = true;
        _chatDragPointerId = evt.pointerId;
        _chatDragPointerOffset = (Vector2)evt.position - new Vector2(chatBounds.xMin, chatBounds.yMin);
        _chatHeader.CapturePointer(evt.pointerId);
        evt.StopPropagation();
    }

    private void OnChatHeaderPointerMove(PointerMoveEvent evt)
    {
        if (!_isDraggingChat || evt.pointerId != _chatDragPointerId || _chatRoot == null)
        {
            return;
        }

        Vector2 desiredTopLeft = (Vector2)evt.position - _chatDragPointerOffset;
        SetChatPosition(desiredTopLeft);
        evt.StopPropagation();
    }

    private void OnChatHeaderPointerUp(PointerUpEvent evt)
    {
        if (!_isDraggingChat || evt.pointerId != _chatDragPointerId)
        {
            return;
        }

        StopChatDrag();
        evt.StopPropagation();
    }

    private void OnChatHeaderPointerCancel(PointerCancelEvent evt)
    {
        if (!_isDraggingChat || evt.pointerId != _chatDragPointerId)
        {
            return;
        }

        StopChatDrag();
        evt.StopPropagation();
    }

    private void StopChatDrag()
    {
        if (_chatHeader != null && _chatDragPointerId >= 0 && _chatHeader.HasPointerCapture(_chatDragPointerId))
        {
            _chatHeader.ReleasePointer(_chatDragPointerId);
        }

        _isDraggingChat = false;
        _chatDragPointerId = -1;
        _chatDragPointerOffset = Vector2.zero;
    }

    private void SetChatPosition(Vector2 desiredTopLeft)
    {
        if (_chatRoot == null)
        {
            return;
        }

        VisualElement clampRoot = _resolvedDocument != null ? _resolvedDocument.rootVisualElement : null;
        if (clampRoot == null)
        {
            _chatRoot.style.left = desiredTopLeft.x;
            _chatRoot.style.top = desiredTopLeft.y;
            return;
        }

        Rect rootBounds = clampRoot.worldBound;
        Rect chatBounds = _chatRoot.worldBound;

        float minLeft = rootBounds.xMin;
        float maxLeft = Mathf.Max(minLeft, rootBounds.xMax - chatBounds.width);
        float minTop = rootBounds.yMin;
        float maxTop = Mathf.Max(minTop, rootBounds.yMax - chatBounds.height);

        float clampedLeft = Mathf.Clamp(desiredTopLeft.x, minLeft, maxLeft);
        float clampedTop = Mathf.Clamp(desiredTopLeft.y, minTop, maxTop);

        _chatRoot.style.left = clampedLeft;
        _chatRoot.style.top = clampedTop;
    }

    private void CycleChannel()
    {
        ChatService.ChatChannel nextChannel = _activeChannel switch
        {
            ChatService.ChatChannel.Global => ChatService.ChatChannel.Local,
            ChatService.ChatChannel.Local => ChatService.ChatChannel.Party,
            ChatService.ChatChannel.Party => ChatService.ChatChannel.Guild,
            _ => ChatService.ChatChannel.Global,
        };

        OnChannelSelected(nextChannel);
    }

    private void SendFromInput()
    {
        if (_inputField == null)
        {
            return;
        }

        string text = _inputField.value;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        ChatService service = ChatService.Instance;
        if (service == null)
        {
            return;
        }

        if (service.Send(_activeChannel, text))
        {
            _inputField.value = string.Empty;
            BlurInputField();
        }
    }

    private void BlurInputField()
    {
        _allowNextInputFocus = false;
        _inputField?.Blur();
    }

    private void FocusInputFieldFromUserIntent()
    {
        if (_inputField == null)
        {
            return;
        }

        _allowNextInputFocus = true;
        _inputField.Focus();
    }

    private void OnInputFieldPointerDown(PointerDownEvent evt)
    {
        if (!IsInputFocused())
        {
            _allowNextInputFocus = true;
        }
    }

    private void OnInputFieldFocusIn(FocusInEvent evt)
    {
        if (_allowNextInputFocus)
        {
            _allowNextInputFocus = false;
            return;
        }

        // Ignore unexpected focus changes so gameplay keys do not leak into chat.
        BlurInputField();
    }

    /// <summary>
    /// Listens at the document root level: if the player clicks anywhere
    /// outside the chat panel, blur the chat input so WASD/game input
    /// is no longer swallowed.
    /// </summary>
    private void RegisterRootPointerDown()
    {
        if (_resolvedDocument == null || _resolvedDocument.rootVisualElement == null)
        {
            return;
        }

        _resolvedDocument.rootVisualElement.UnregisterCallback<PointerDownEvent>(OnDocumentRootPointerDown);
        _resolvedDocument.rootVisualElement.RegisterCallback<PointerDownEvent>(OnDocumentRootPointerDown);
    }

    private void OnDocumentRootPointerDown(PointerDownEvent evt)
    {
        if (!IsInputFocused())
        {
            return;
        }

        // Walk up from the click target to see if it's inside the chat root.
        var target = evt.target as VisualElement;
        while (target != null)
        {
            if (ReferenceEquals(target, _chatRoot))
            {
                return; // Click is inside chat — keep focus.
            }

            target = target.parent;
        }

        // Click landed outside the chat panel — blur the input.
        BlurInputField();
    }

    private void OnChatMessageReceived(ChatService.ChatMessage message)
    {
        if (_chatRoot == null || _messagesContent == null)
        {
            TryBindUi();
        }

        if (_messagesContent == null)
        {
            return;
        }

        AppendMessage(message);
        if (_messagesContent.childCount > MaxRenderedRows)
        {
            _messagesContent.RemoveAt(0);
        }

        if (_messagesScroll != null)
        {
            _messagesScroll.scrollOffset = new Vector2(_messagesScroll.scrollOffset.x, float.MaxValue);
        }

        if (!_isOpen)
        {
            _unreadCount = Mathf.Min(_unreadCount + 1, 99);
            RefreshUnreadBadge();
        }
    }

    private void RebuildFromHistory()
    {
        if (_messagesContent == null)
        {
            return;
        }

        _messagesContent.Clear();
        ChatService service = ChatService.Instance;
        if (service == null)
        {
            return;
        }

        IReadOnlyList<ChatService.ChatMessage> history = service.History;
        int startIndex = Mathf.Max(0, history.Count - MaxRenderedRows);
        for (int i = startIndex; i < history.Count; i++)
        {
            AppendMessage(history[i]);
        }
    }

    private void AppendMessage(ChatService.ChatMessage message)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("mmo-chat-line");

        Label time = new Label(message.TimestampUtc.ToLocalTime().ToString("HH:mm"));
        time.enableRichText = false;
        time.AddToClassList("mmo-chat-line-time");
        row.Add(time);

        Label channel = new Label($"[{ChannelToShortTag(message.Channel)}]");
        channel.enableRichText = false;
        channel.AddToClassList("mmo-chat-line-channel");
        row.Add(channel);

        Label sender = new Label($"{UiTextSanitizer.SanitizeForLabel(message.SenderName, collapseWhitespace: true)}:");
        sender.enableRichText = false;
        sender.AddToClassList("mmo-chat-line-sender");
        if (message.IsSystem)
        {
            sender.AddToClassList("mmo-chat-line-system");
        }
        row.Add(sender);

        Label text = new Label(UiTextSanitizer.SanitizeForLabel(message.Text, collapseWhitespace: true));
        text.enableRichText = false;
        text.AddToClassList("mmo-chat-line-text");
        text.style.whiteSpace = WhiteSpace.Normal;
        if (message.IsSystem)
        {
            text.AddToClassList("mmo-chat-line-system");
        }
        row.Add(text);

        _messagesContent.Add(row);
    }

    private static string ChannelToShortTag(ChatService.ChatChannel channel)
    {
        return channel switch
        {
            ChatService.ChatChannel.Global => "GLOBAL",
            ChatService.ChatChannel.Local => "LOCAL",
            ChatService.ChatChannel.Party => "PARTY",
            ChatService.ChatChannel.Guild => "GUILD",
            _ => "SYSTEM",
        };
    }

    private void RefreshUnreadBadge()
    {
        if (_chatUnreadBadge == null)
        {
            return;
        }

        bool hasUnread = _unreadCount > 0 && !_isOpen;
        _chatUnreadBadge.style.display = hasUnread ? DisplayStyle.Flex : DisplayStyle.None;
        _chatUnreadBadge.text = _unreadCount > 99 ? "99+" : _unreadCount.ToString();
    }
}
