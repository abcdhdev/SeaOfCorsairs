using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
public sealed class LoginOverlayController : MonoBehaviour
{
    private const string UxmlResourcePath = "LoginOverlay/LoginOverlay";
    private const string UssResourcePath = "LoginOverlay/LoginOverlay";
    private const string MainHudRootName = "HudRoot";
    private const string MetaRootName = "MetaRoot";
    private const float DefaultConnectTimeoutSeconds = 15f;

    // Used by gameplay systems (camera, input, etc) to ignore in-world controls while meta UI is active.
    public static bool IsMetaUiActive { get; private set; } = true;

    private enum OverlayView
    {
        Login = 0,
        Connecting = 1,
    }

    private enum AuthMode
    {
        Login = 0,
        Register = 1,
    }

    private UIDocument _mainUiDocument;
    private VisualElement _overlayRoot;

    private Foldout _advancedFoldout;
    private TextField _authBaseUrlField;
    private TextField _playerDataBaseUrlField;
    private TextField _emailField;
    private TextField _passwordField;
    private TextField _displayNameField;
    private Button _submitButton;
    private Button _modeToggleButton;
    private Label _statusLabel; // Login view status

    private DropdownField _savedAccountsDropdown;
    private Toggle _rememberMeToggle;
    private List<SavedAccount> _savedAccounts;

    private VisualElement _loginPanel;
    private VisualElement _connectingPanel;
    private Label _connectingStatusLabel;
    private Button _cancelConnectButton;

    private AuthMode _mode = AuthMode.Login;
    private OverlayView _view = OverlayView.Login;
    private bool _isBusy;
    private bool _isAwaitingInWorld;
    private bool _connectCancelRequested;
    private Coroutine _connectTimeoutCoroutine;
    private CancellationTokenSource _lifetimeCts;
    private InputHandler _cachedInputHandler;
    private bool _cachedInputHandlerEnabled;
    private bool _netcodeEventsHooked;

    private void Awake()
    {
        if (Application.isBatchMode)
        {
            enabled = false;
            return;
        }

        BackendSession.LoadFromPlayerPrefs();
    }

    private void OnEnable()
    {
        if (_lifetimeCts == null)
        {
            _lifetimeCts = new CancellationTokenSource();
        }
    }

    private void OnDisable()
    {
        _lifetimeCts?.Cancel();
        _lifetimeCts?.Dispose();
        _lifetimeCts = null;

        UnhookNetcodeEvents();
        UnhookInWorldEvents();
        StopConnectTimeout();
        _isAwaitingInWorld = false;
        _connectCancelRequested = false;
        IsMetaUiActive = false;
        DetachOverlay();
    }

    private async void Start()
    {
        EnsureOverlayAttached();

        // Best-effort silent sign-in when a refresh token exists.
        try
        {
            await TryAutoLoginAsync(_lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"LoginOverlayController: Auto-login failed: {ex.Message}", this);
        }
    }

    private void EnsureOverlayAttached()
    {
        if (_overlayRoot != null && _overlayRoot.panel != null)
        {
            return;
        }

        _mainUiDocument = FindMainHudDocument();
        if (_mainUiDocument == null || _mainUiDocument.rootVisualElement == null)
        {
            Debug.LogWarning("LoginOverlayController: Could not find main HUD UIDocument (with HudRoot).", this);
            return;
        }

        var visualTree = Resources.Load<VisualTreeAsset>(UxmlResourcePath);
        if (visualTree == null)
        {
            Debug.LogWarning($"LoginOverlayController: Missing UXML resource '{UxmlResourcePath}'.", this);
            return;
        }

        var container = visualTree.Instantiate();
        _overlayRoot = container.Q<VisualElement>("LoginOverlayRoot") ?? container;
        _overlayRoot.pickingMode = PickingMode.Position;

        var styleSheet = Resources.Load<StyleSheet>(UssResourcePath);
        if (styleSheet != null)
        {
            _overlayRoot.styleSheets.Add(styleSheet);
        }

        VisualElement attachTarget = _mainUiDocument.rootVisualElement.Q<VisualElement>(MetaRootName) ?? _mainUiDocument.rootVisualElement;
        attachTarget.Add(_overlayRoot);
        _overlayRoot.BlockRaycasts();

        BindUiElements();
        LoadDefaultsIntoUi();
        ApplyModeToUi();
        ShowLogin(string.Empty);
        RegisterUiCallbacks();
        SetOverlayVisible(true);
        HookNetcodeEvents();
    }

    private void DetachOverlay()
    {
        if (_overlayRoot == null)
        {
            return;
        }

        UnhookNetcodeEvents();
        UnhookInWorldEvents();
        StopConnectTimeout();
        _isAwaitingInWorld = false;
        _connectCancelRequested = false;
        SetGameplayInputEnabled(true);
        UnregisterUiCallbacks();
        _overlayRoot.AllowRaycasts();

        if (_overlayRoot.parent != null)
        {
            _overlayRoot.parent.Remove(_overlayRoot);
        }

        _advancedFoldout = null;
        _authBaseUrlField = null;
        _playerDataBaseUrlField = null;
        _emailField = null;
        _passwordField = null;
        _displayNameField = null;
        _submitButton = null;
        _modeToggleButton = null;
        _statusLabel = null;
        _savedAccountsDropdown = null;
        _rememberMeToggle = null;
        _savedAccounts = null;
        _loginPanel = null;
        _connectingPanel = null;
        _connectingStatusLabel = null;
        _cancelConnectButton = null;

        _overlayRoot = null;
        _mainUiDocument = null;
    }

    private void BindUiElements()
    {
        if (_overlayRoot == null)
        {
            return;
        }

        _loginPanel = _overlayRoot.Q<VisualElement>("LoginPanel");
        _connectingPanel = _overlayRoot.Q<VisualElement>("ConnectingPanel");
        _connectingStatusLabel = _overlayRoot.Q<Label>("ConnectingStatusLabel");
        _cancelConnectButton = _overlayRoot.Q<Button>("CancelConnectButton");

        _advancedFoldout = _overlayRoot.Q<Foldout>("AdvancedFoldout");
        _authBaseUrlField = _overlayRoot.Q<TextField>("AuthBaseUrlField");
        _playerDataBaseUrlField = _overlayRoot.Q<TextField>("PlayerDataBaseUrlField");
        _emailField = _overlayRoot.Q<TextField>("EmailField");
        _passwordField = _overlayRoot.Q<TextField>("PasswordField");
        _displayNameField = _overlayRoot.Q<TextField>("DisplayNameField");
        _submitButton = _overlayRoot.Q<Button>("SubmitButton");
        _modeToggleButton = _overlayRoot.Q<Button>("ModeToggleButton");
        _statusLabel = _overlayRoot.Q<Label>("StatusLabel");

        _savedAccountsDropdown = _overlayRoot.Q<DropdownField>("SavedAccountsDropdown");
        _rememberMeToggle = _overlayRoot.Q<Toggle>("RememberMeToggle");

        if (_passwordField != null)
        {
            _passwordField.isPasswordField = true;
            _passwordField.maskChar = '*';
        }
    }

    private void LoadDefaultsIntoUi()
    {
        if (_authBaseUrlField != null)
        {
            _authBaseUrlField.value = string.IsNullOrWhiteSpace(BackendSession.AuthBaseUrl)
                ? BackendSession.DefaultAuthBaseUrl
                : BackendSession.AuthBaseUrl;
        }

        if (_playerDataBaseUrlField != null)
        {
            _playerDataBaseUrlField.value = string.IsNullOrWhiteSpace(BackendSession.PlayerDataBaseUrl)
                ? BackendSession.DefaultPlayerDataBaseUrl
                : BackendSession.PlayerDataBaseUrl;
        }

        if (_emailField != null)
        {
            _emailField.value = BackendSession.LastEmail ?? string.Empty;
        }

        if (_displayNameField != null)
        {
            _displayNameField.value = string.Empty;
        }

        SetStatus(string.Empty);

        _savedAccounts = BackendSession.GetSavedAccounts();
        if (_savedAccountsDropdown != null)
        {
            var choices = new List<string> { "Select an account..." };
            foreach (var acc in _savedAccounts)
            {
                choices.Add(acc.Email);
            }
            _savedAccountsDropdown.choices = choices;
            if (choices.Count > 1)
            {
                _savedAccountsDropdown.style.display = DisplayStyle.Flex;
                _savedAccountsDropdown.value = choices[0];
            }
            else
            {
                _savedAccountsDropdown.style.display = DisplayStyle.None;
            }
        }
    }

    private void RegisterUiCallbacks()
    {
        if (_submitButton != null)
        {
            _submitButton.clicked += OnSubmitClicked;
        }

        if (_modeToggleButton != null)
        {
            _modeToggleButton.clicked += ToggleMode;
        }

        if (_cancelConnectButton != null)
        {
            _cancelConnectButton.clicked += OnCancelConnectClicked;
        }

        if (_passwordField != null)
        {
            _passwordField.RegisterCallback<KeyDownEvent>(OnPasswordKeyDown);
        }

        if (_displayNameField != null)
        {
            _displayNameField.RegisterCallback<KeyDownEvent>(OnDisplayNameKeyDown);
        }

        if (_savedAccountsDropdown != null)
        {
            _savedAccountsDropdown.RegisterValueChangedCallback(OnSavedAccountChanged);
        }
    }

    private void UnregisterUiCallbacks()
    {
        if (_submitButton != null)
        {
            _submitButton.clicked -= OnSubmitClicked;
        }

        if (_modeToggleButton != null)
        {
            _modeToggleButton.clicked -= ToggleMode;
        }

        if (_cancelConnectButton != null)
        {
            _cancelConnectButton.clicked -= OnCancelConnectClicked;
        }

        if (_passwordField != null)
        {
            _passwordField.UnregisterCallback<KeyDownEvent>(OnPasswordKeyDown);
        }

        if (_displayNameField != null)
        {
            _displayNameField.UnregisterCallback<KeyDownEvent>(OnDisplayNameKeyDown);
        }

        if (_savedAccountsDropdown != null)
        {
            _savedAccountsDropdown.UnregisterValueChangedCallback(OnSavedAccountChanged);
        }
    }

    private void OnSavedAccountChanged(ChangeEvent<string> evt)
    {
        if (evt.newValue == "Select an account...") return;
        var acc = _savedAccounts?.Find(a => a.Email == evt.newValue);
        if (acc != null)
        {
            if (_emailField != null) _emailField.value = acc.Email;
            if (_passwordField != null) _passwordField.value = acc.Password;
            if (_rememberMeToggle != null) _rememberMeToggle.value = true;
        }
    }

    private void OnDestroy()
    {
        UnregisterUiCallbacks();
    }

    private void ToggleMode()
    {
        if (_isBusy)
        {
            return;
        }

        _mode = _mode == AuthMode.Login ? AuthMode.Register : AuthMode.Login;
        ApplyModeToUi();
        SetStatus(string.Empty);
    }

    private void ApplyModeToUi()
    {
        if (_submitButton != null)
        {
            _submitButton.text = _mode == AuthMode.Login ? "Login" : "Create Account";
        }

        if (_modeToggleButton != null)
        {
            _modeToggleButton.text = _mode == AuthMode.Login ? "Create Account" : "Back to Login";
        }

        if (_displayNameField != null)
        {
            _displayNameField.style.display = _mode == AuthMode.Register ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void OnPasswordKeyDown(KeyDownEvent evt)
    {
        if (_isBusy)
        {
            return;
        }

        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
        {
            evt.StopPropagation();
            OnSubmitClicked();
        }
    }

    private void OnDisplayNameKeyDown(KeyDownEvent evt)
    {
        if (_isBusy)
        {
            return;
        }

        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
        {
            evt.StopPropagation();
            OnSubmitClicked();
        }
    }

    private async void OnSubmitClicked()
    {
        if (_isBusy)
        {
            return;
        }

        EnsureOverlayAttached();
        if (_overlayRoot == null)
        {
            return;
        }

        var authBaseUrl = BackendSession.GetAuthBaseUrlOrDefault(_authBaseUrlField != null ? _authBaseUrlField.value : null);
        var playerBaseUrl = BackendSession.GetPlayerDataBaseUrlOrDefault(_playerDataBaseUrlField != null ? _playerDataBaseUrlField.value : null);
        BackendSession.SaveBaseUrls(authBaseUrl, playerBaseUrl);

        var email = _emailField != null ? _emailField.value : string.Empty;
        var password = _passwordField != null ? _passwordField.value : string.Empty;
        var displayName = _displayNameField != null ? _displayNameField.value : string.Empty;

        email = (email ?? string.Empty).Trim();
        password ??= string.Empty;
        displayName ??= string.Empty;

        BackendSession.SaveLastEmail(email);

        if (string.IsNullOrWhiteSpace(email))
        {
            SetStatus("Email is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            SetStatus("Password is required.");
            return;
        }

        if (_mode == AuthMode.Register)
        {
            if (password.Length < 8)
            {
                SetStatus("Password must be at least 8 characters.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(displayName) && displayName.Trim().Length > 64)
            {
                SetStatus("Display name must be 64 characters or fewer.");
                return;
            }
        }

        SetBusy(true);
        SetStatus(_mode == AuthMode.Login ? "Signing in..." : "Creating account...");

        try
        {
            var client = new BackendAuthClient(BackendSession.AuthBaseUrl);
            var ct = _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None;

            TokenResponse tokens = _mode == AuthMode.Login
                ? await client.LoginAsync(email, password, ct)
                : await client.RegisterAsync(email, password, displayName, ct);

            if (tokens == null || string.IsNullOrWhiteSpace(tokens.accessToken) || string.IsNullOrWhiteSpace(tokens.refreshToken))
            {
                SetStatus("Login failed: missing token response.");
                return;
            }

            BackendSession.SetTokens(tokens.accessToken, tokens.refreshToken, tokens.expiresInSeconds);
            ApplyNetcodeConnectionData(tokens.accessToken, tokens.refreshToken);

            if (_rememberMeToggle != null && _rememberMeToggle.value)
            {
                BackendSession.SaveAccount(email, password);
            }

            // Validate + get display info (best-effort).
            MeResponse me = null;
            try
            {
                me = await client.MeAsync(tokens.accessToken, ct);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"LoginOverlayController: /v1/auth/me failed: {ex.Message}", this);
            }

            var label = me != null && !string.IsNullOrWhiteSpace(me.displayName)
                ? me.displayName
                : email;

            // Best-effort: touch Player Data service to ensure the account has a state row created.
            try
            {
                var playerData = new BackendPlayerDataClient(BackendSession.PlayerDataBaseUrl);
                _ = await playerData.GetPlayerMeRawAsync(tokens.accessToken, ct);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"LoginOverlayController: /v1/player/me failed: {ex.Message}", this);
            }

            SetStatus($"Welcome, {label}.");
            if (StartNetcodeClientIfNeeded())
            {
                BeginAwaitingInWorld("Connecting to the server...");
            }
            else
            {
                ReturnToLogin("Failed to start game client. Check server address/port and try again.");
            }
        }
        catch (BackendApiException ex)
        {
            SetStatus(ex.Message);

            // If our stored refresh token is no longer valid, clear persisted tokens.
            if (ex.StatusCode == 401)
            {
                BackendSession.ClearTokens();
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Login failed: {ex.Message}");
        }
        finally
        {
            if (!_isAwaitingInWorld)
            {
                SetBusy(false);
            }
        }
    }

    private async Task TryAutoLoginAsync(CancellationToken cancellationToken)
    {
        if (!BackendSession.HasRefreshToken)
        {
            return;
        }

        var client = new BackendAuthClient(BackendSession.AuthBaseUrl);

        // If the access token is present and not expired, try using it first.
        if (BackendSession.HasAccessToken && !BackendSession.IsAccessTokenExpiredOrMissing())
        {
            try
            {
                _ = await client.MeAsync(BackendSession.AccessToken, cancellationToken);
                ApplyNetcodeConnectionData(BackendSession.AccessToken, BackendSession.RefreshToken);

                try
                {
                    var playerData = new BackendPlayerDataClient(BackendSession.PlayerDataBaseUrl);
                    _ = await playerData.GetPlayerMeRawAsync(BackendSession.AccessToken, cancellationToken);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"LoginOverlayController: /v1/player/me failed: {ex.Message}", this);
                }

                if (StartNetcodeClientIfNeeded())
                {
                    BeginAwaitingInWorld("Connecting to the server...");
                }
                else
                {
                    ReturnToLogin("Failed to start game client. Check server address/port and try again.");
                }
                return;
            }
            catch (BackendApiException ex) when (ex.StatusCode == 401)
            {
                // Fall through to refresh.
            }
        }

        // Attempt refresh.
        try
        {
            var refreshed = await client.RefreshAsync(BackendSession.RefreshToken, cancellationToken);
            if (refreshed == null || string.IsNullOrWhiteSpace(refreshed.accessToken) || string.IsNullOrWhiteSpace(refreshed.refreshToken))
            {
                return;
            }

            BackendSession.SetTokens(refreshed.accessToken, refreshed.refreshToken, refreshed.expiresInSeconds);
            ApplyNetcodeConnectionData(refreshed.accessToken, refreshed.refreshToken);

            _ = await client.MeAsync(refreshed.accessToken, cancellationToken);

            try
            {
                var playerData = new BackendPlayerDataClient(BackendSession.PlayerDataBaseUrl);
                _ = await playerData.GetPlayerMeRawAsync(refreshed.accessToken, cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"LoginOverlayController: /v1/player/me failed: {ex.Message}", this);
            }

            if (StartNetcodeClientIfNeeded())
            {
                BeginAwaitingInWorld("Connecting to the server...");
            }
            else
            {
                ReturnToLogin("Failed to start game client. Check server address/port and try again.");
            }
        }
        catch (BackendApiException ex) when (ex.StatusCode == 401)
        {
            BackendSession.ClearTokens();
        }
        catch (Exception ex)
        {
            // Backend probably isn't running yet; keep overlay visible and let the user click Login.
            Debug.LogWarning($"LoginOverlayController: Auto refresh failed: {ex.Message}", this);
        }
    }

    private void SetOverlayVisible(bool visible)
    {
        if (_overlayRoot == null)
        {
            return;
        }

        _overlayRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        IsMetaUiActive = visible;
        if (visible)
        {
            _overlayRoot.BlockRaycasts();
            SetGameplayInputEnabled(false);
            if (!_isAwaitingInWorld && _view == OverlayView.Login)
            {
                _emailField?.Focus();
            }
        }
        else
        {
            _overlayRoot.AllowRaycasts();
            SetGameplayInputEnabled(true);
        }
    }

    private void BeginAwaitingInWorld(string status)
    {
        _isAwaitingInWorld = true;
        _connectCancelRequested = false;

        // Keep the meta overlay visible until we have a local player spawned.
        SetBusy(true);
        SetOverlayVisible(true);
        ShowConnecting(status ?? "Connecting to the server...");

        HookInWorldEvents();
        StartConnectTimeout(DefaultConnectTimeoutSeconds);

        // If we already have the local player, drop the overlay immediately.
        if (Player.LocalPlayer != null)
        {
            EndAwaitingInWorld();
        }
    }

    private void EndAwaitingInWorld()
    {
        _isAwaitingInWorld = false;
        StopConnectTimeout();
        SetBusy(false);
        ShowLogin(string.Empty);
        SetOverlayVisible(false);
        UnhookInWorldEvents();
    }

    private void HookInWorldEvents()
    {
        Player.LocalPlayerSpawned -= OnLocalPlayerSpawned;
        Player.LocalPlayerSpawned += OnLocalPlayerSpawned;
    }

    private void UnhookInWorldEvents()
    {
        Player.LocalPlayerSpawned -= OnLocalPlayerSpawned;
    }

    private void OnLocalPlayerSpawned(Player player)
    {
        if (!_isAwaitingInWorld)
        {
            return;
        }

        EndAwaitingInWorld();
    }

    private void HookNetcodeEvents()
    {
        if (_netcodeEventsHooked)
        {
            return;
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnNetcodeClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnNetcodeClientDisconnected;
        }

        _netcodeEventsHooked = true;
    }

    private void UnhookNetcodeEvents()
    {
        if (!_netcodeEventsHooked)
        {
            return;
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnNetcodeClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnNetcodeClientDisconnected;
        }

        _netcodeEventsHooked = false;
    }

    private void OnNetcodeClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null || clientId != NetworkManager.Singleton.LocalClientId)
        {
            return;
        }

        if (_isAwaitingInWorld)
        {
            SetConnectingStatus("Connected. Spawning player...");
        }
    }

    private void OnNetcodeClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null || clientId != NetworkManager.Singleton.LocalClientId)
        {
            return;
        }

        if (_connectCancelRequested)
        {
            _connectCancelRequested = false;
            return;
        }

        string reason = NetworkManager.Singleton.DisconnectReason;
        ReturnToLogin(string.IsNullOrWhiteSpace(reason) ? "Disconnected from server." : $"Disconnected: {reason}");
    }

    private void SetGameplayInputEnabled(bool enabled)
    {
        if (_cachedInputHandler == null)
        {
            _cachedInputHandler = FindFirstObjectByType<InputHandler>();
            if (_cachedInputHandler != null)
            {
                _cachedInputHandlerEnabled = _cachedInputHandler.enabled;
            }
        }

        // Only restore to the original state when re-enabling.
        if (!enabled)
        {
            if (_cachedInputHandler != null)
            {
                _cachedInputHandler.enabled = false;
            }
        }
        else
        {
            if (_cachedInputHandler != null)
            {
                _cachedInputHandler.enabled = _cachedInputHandlerEnabled;
            }
        }
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;

        _advancedFoldout?.SetEnabled(!isBusy);
        _authBaseUrlField?.SetEnabled(!isBusy);
        _playerDataBaseUrlField?.SetEnabled(!isBusy);
        _emailField?.SetEnabled(!isBusy);
        _passwordField?.SetEnabled(!isBusy);
        _displayNameField?.SetEnabled(!isBusy);
        _modeToggleButton?.SetEnabled(!isBusy);
        _submitButton?.SetEnabled(!isBusy);
        _savedAccountsDropdown?.SetEnabled(!isBusy);
        _rememberMeToggle?.SetEnabled(!isBusy);

        // Always allow cancel while in the connecting view.
        _cancelConnectButton?.SetEnabled(_isAwaitingInWorld);
    }

    private void SetStatus(string message)
    {
        if (_statusLabel != null)
        {
            _statusLabel.enableRichText = false;
            _statusLabel.text = UiTextSanitizer.SanitizeForLabel(message ?? string.Empty, collapseWhitespace: true);
        }
    }

    private void SetConnectingStatus(string message)
    {
        if (_connectingStatusLabel == null)
        {
            return;
        }

        _connectingStatusLabel.text = string.IsNullOrWhiteSpace(message)
            ? string.Empty
            : UiTextSanitizer.SanitizeForLabel(message, collapseWhitespace: true);
        _connectingStatusLabel.enableRichText = false;
    }

    private void ShowLogin(string statusMessage)
    {
        _view = OverlayView.Login;

        if (_loginPanel != null)
        {
            _loginPanel.style.display = DisplayStyle.Flex;
        }

        if (_connectingPanel != null)
        {
            _connectingPanel.style.display = DisplayStyle.None;
        }

        SetConnectingStatus(string.Empty);
        SetStatus(statusMessage ?? string.Empty);
    }

    private void ShowConnecting(string statusMessage)
    {
        _view = OverlayView.Connecting;

        if (_loginPanel != null)
        {
            _loginPanel.style.display = DisplayStyle.None;
        }

        if (_connectingPanel != null)
        {
            _connectingPanel.style.display = DisplayStyle.Flex;
        }

        SetStatus(string.Empty);
        SetConnectingStatus(statusMessage);
    }

    private void ReturnToLogin(string message)
    {
        StopConnectTimeout();

        _isAwaitingInWorld = false;
        UnhookInWorldEvents();

        SetBusy(false);
        ShowLogin(message);
        SetOverlayVisible(true);

        // Ensure focus returns to the email field when we come back.
        _emailField?.Focus();
    }

    private void OnCancelConnectClicked()
    {
        if (!_isAwaitingInWorld)
        {
            return;
        }

        _connectCancelRequested = true;
        StopConnectTimeout();

        _isAwaitingInWorld = false;
        UnhookInWorldEvents();

        try
        {
            if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
            {
                NetworkManager.Singleton.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"LoginOverlayController: Cancel connect shutdown failed: {ex.Message}", this);
        }

        SetBusy(false);
        ShowLogin("Canceled.");
        SetOverlayVisible(true);
        _emailField?.Focus();
    }

    private void StartConnectTimeout(float seconds)
    {
        StopConnectTimeout();

        if (seconds <= 0f)
        {
            return;
        }

        _connectTimeoutCoroutine = StartCoroutine(ConnectTimeoutRoutine(seconds));
    }

    private void StopConnectTimeout()
    {
        if (_connectTimeoutCoroutine != null)
        {
            StopCoroutine(_connectTimeoutCoroutine);
            _connectTimeoutCoroutine = null;
        }
    }

    private IEnumerator ConnectTimeoutRoutine(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);

        if (!_isAwaitingInWorld)
        {
            yield break;
        }

        // Suppress the disconnect callback from overriding our timeout UI.
        _connectCancelRequested = true;

        try
        {
            if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
            {
                NetworkManager.Singleton.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"LoginOverlayController: Timeout shutdown failed: {ex.Message}", this);
        }

        ReturnToLogin($"Connection timed out after {seconds:0.#}s.");
    }

    private static void ApplyNetcodeConnectionData(string accessToken, string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        if (NetworkManager.Singleton == null || NetworkManager.Singleton.NetworkConfig == null)
        {
            return;
        }

        EnsureConnectionApprovalEnabled();

        // ConnectionData is sent to the server and is available in ConnectionApprovalRequest.Payload.
        NetworkManager.Singleton.NetworkConfig.ConnectionData = NetcodeConnectionPayload.Build(accessToken, refreshToken);
    }

    private static bool StartNetcodeClientIfNeeded()
    {
        if (NetworkManager.Singleton == null)
        {
            return false;
        }

        if (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer)
        {
            return true;
        }

        EnsureConnectionApprovalEnabled();

#if UNITY_EDITOR
        bool started = NetworkManager.Singleton.StartHost();
        if (!started)
        {
            Debug.LogWarning("LoginOverlayController: Failed to start Netcode host in Editor.");
            return false;
        }
#else
        bool started = NetworkManager.Singleton.StartClient();
        if (!started)
        {
            Debug.LogWarning("LoginOverlayController: Failed to start Netcode client.");
            return false;
        }
#endif

        return true;
    }

    private static void EnsureConnectionApprovalEnabled()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.NetworkConfig == null)
        {
            return;
        }

        // Client and server must agree on this flag so the connection request payload shape matches.
        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
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
            if (doc == null)
            {
                continue;
            }

            var root = doc.rootVisualElement;
            if (root == null)
            {
                continue;
            }

            if (root.Q<VisualElement>(MainHudRootName) != null)
            {
                return doc;
            }
        }

        return docs[0];
    }
}
