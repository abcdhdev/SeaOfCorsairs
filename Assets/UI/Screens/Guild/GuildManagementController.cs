using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class GuildManagementController : IDisposable
{
    private const string SharedPanelStyleResourcePath = "Shared/OverlayPanel";
    private const string UxmlResourcePath = "Guild/GuildManagement";
    private const string UssResourcePath = "Guild/GuildManagement";
    private const string DefaultStatusMessage = "Browse the guild roster or found a new guild with a 3-letter banner for your crew.";
    private const string StatusSuccessClass = "guild-status-success";
    private const string StatusErrorClass = "guild-status-error";
    private const string CurrentGuildCardClass = "guild-list-card-current";
    private const int GuildAbbreviationLength = 3;

    private readonly VisualElement attachTarget;

    private VisualElement overlayRoot;
    private VisualElement panelRoot;
    private VisualElement headerRoot;
    private VisualElement createFormRoot;
    private ScrollView guildListScrollView;
    private Label currentGuildNameLabel;
    private Label currentGuildMetaLabel;
    private Label totalGuildCountLabel;
    private Label createAvailabilityLabel;
    private Label statusLabel;
    private Button refreshButton;
    private Button createToggleButton;
    private Button submitCreateButton;
    private Button cancelCreateButton;
    private Button closeButton;
    private TextField nameField;
    private TextField tagField;
    private TextField descriptionField;
    private DraggableWindowController panelDragController;

    private CancellationTokenSource lifetimeCts = new();
    private CancellationTokenSource operationCts;
    private GuildSummaryResponse[] cachedGuilds = Array.Empty<GuildSummaryResponse>();
    private string currentGuildId = string.Empty;
    private bool hasLoadedGuilds;
    private bool isLoadingGuilds;
    private bool isCreatingGuild;

    public GuildManagementController(VisualElement attachTarget)
    {
        this.attachTarget = attachTarget;
    }

    public bool IsVisible => overlayRoot != null && overlayRoot.resolvedStyle.display != DisplayStyle.None;
    public VisualElement OverlayRoot => overlayRoot;

    public void Attach()
    {
        if (attachTarget == null || overlayRoot != null)
        {
            return;
        }

        VisualTreeAsset visualTree = Resources.Load<VisualTreeAsset>(UxmlResourcePath);
        if (visualTree == null)
        {
            Debug.LogWarning($"GuildManagementController: Missing UXML resource '{UxmlResourcePath}'.");
            return;
        }

        TemplateContainer container = visualTree.Instantiate();
        overlayRoot = container.Q<VisualElement>("GuildManagementOverlay") ?? container;
        if (!ReferenceEquals(overlayRoot, container))
        {
            overlayRoot.RemoveFromHierarchy();
        }

        overlayRoot.pickingMode = PickingMode.Position;

        StyleSheet sharedPanelStyle = Resources.Load<StyleSheet>(SharedPanelStyleResourcePath);
        if (sharedPanelStyle != null)
        {
            overlayRoot.styleSheets.Add(sharedPanelStyle);
        }

        StyleSheet panelStyle = Resources.Load<StyleSheet>(UssResourcePath);
        if (panelStyle != null)
        {
            overlayRoot.styleSheets.Add(panelStyle);
        }

        attachTarget.Add(overlayRoot);
        overlayRoot.BlockRaycasts();

        BindUiElements();
        panelDragController = new DraggableWindowController(overlayRoot, panelRoot, headerRoot, closeButton);
        RegisterCallbacks();
        SetCreateFormVisible(false, clearFields: true);
        SetStatus(DefaultStatusMessage, isSuccess: false, useTone: false);
        RebuildGuildList();
        Refresh();
        SetVisible(false);
    }

    public void ToggleVisibility()
    {
        if (overlayRoot == null)
        {
            Attach();
        }

        if (overlayRoot == null)
        {
            return;
        }

        if (IsVisible)
        {
            Hide();
            return;
        }

        Show();
    }

    public void Show()
    {
        if (overlayRoot == null)
        {
            Attach();
        }

        if (overlayRoot == null)
        {
            return;
        }

        SetVisible(true);
        Refresh();

        if (!hasLoadedGuilds && !isLoadingGuilds)
        {
            _ = RefreshGuildsAsync(preserveStatusMessage: false);
        }
    }

    public void Hide()
    {
        SetVisible(false);
    }

    public void Refresh()
    {
        if (overlayRoot == null)
        {
            return;
        }

        UpdateCurrentGuildIdFromCache();
        UpdateSummaryLabels();
        UpdateButtonStates();
    }

    public void Dispose()
    {
        CancelOperation();

        if (!lifetimeCts.IsCancellationRequested)
        {
            lifetimeCts.Cancel();
        }

        lifetimeCts.Dispose();

        if (overlayRoot == null)
        {
            return;
        }

        panelDragController?.Dispose();
        panelDragController = null;
        UnregisterCallbacks();
        overlayRoot.AllowRaycasts();

        if (overlayRoot.parent != null)
        {
            overlayRoot.parent.Remove(overlayRoot);
        }

        overlayRoot = null;
        panelRoot = null;
        headerRoot = null;
        createFormRoot = null;
        guildListScrollView = null;
        currentGuildNameLabel = null;
        currentGuildMetaLabel = null;
        totalGuildCountLabel = null;
        createAvailabilityLabel = null;
        statusLabel = null;
        refreshButton = null;
        createToggleButton = null;
        submitCreateButton = null;
        cancelCreateButton = null;
        closeButton = null;
        nameField = null;
        tagField = null;
        descriptionField = null;
        cachedGuilds = Array.Empty<GuildSummaryResponse>();
        currentGuildId = string.Empty;
        hasLoadedGuilds = false;
        isLoadingGuilds = false;
        isCreatingGuild = false;
    }

    private void BindUiElements()
    {
        if (overlayRoot == null)
        {
            return;
        }

        panelRoot = overlayRoot.Q<VisualElement>("GuildManagementPanel");
        headerRoot = overlayRoot.Q<VisualElement>("GuildManagementHeader");
        createFormRoot = overlayRoot.Q<VisualElement>("GuildCreateForm");
        guildListScrollView = overlayRoot.Q<ScrollView>("GuildListScrollView");
        currentGuildNameLabel = overlayRoot.Q<Label>("GuildCurrentGuildNameLabel");
        currentGuildMetaLabel = overlayRoot.Q<Label>("GuildCurrentGuildMetaLabel");
        totalGuildCountLabel = overlayRoot.Q<Label>("GuildTotalCountLabel");
        createAvailabilityLabel = overlayRoot.Q<Label>("GuildCreateAvailabilityLabel");
        statusLabel = overlayRoot.Q<Label>("GuildStatusLabel");
        refreshButton = overlayRoot.Q<Button>("GuildRefreshButton");
        createToggleButton = overlayRoot.Q<Button>("GuildCreateToggleButton");
        submitCreateButton = overlayRoot.Q<Button>("GuildSubmitCreateButton");
        cancelCreateButton = overlayRoot.Q<Button>("GuildCancelCreateButton");
        closeButton = overlayRoot.Q<Button>("GuildCloseButton");
        nameField = overlayRoot.Q<TextField>("GuildNameField");
        tagField = overlayRoot.Q<TextField>("GuildTagField");
        descriptionField = overlayRoot.Q<TextField>("GuildDescriptionField");

        if (tagField != null)
        {
            tagField.maxLength = GuildAbbreviationLength;
        }

        if (descriptionField != null)
        {
            descriptionField.multiline = true;
        }
    }

    private void RegisterCallbacks()
    {
        if (overlayRoot != null)
        {
            overlayRoot.RegisterCallback<PointerUpEvent>(OnOverlayPointerUp);
        }

        if (panelRoot != null)
        {
            panelRoot.RegisterCallback<PointerDownEvent>(OnPanelPointerDown);
            panelRoot.RegisterCallback<PointerUpEvent>(OnPanelPointerUp);
        }

        if (closeButton != null)
        {
            closeButton.clicked += OnCloseClicked;
        }

        if (refreshButton != null)
        {
            refreshButton.clicked += OnRefreshClicked;
        }

        if (createToggleButton != null)
        {
            createToggleButton.clicked += OnCreateToggleClicked;
        }

        if (submitCreateButton != null)
        {
            submitCreateButton.clicked += OnSubmitCreateClicked;
        }

        if (cancelCreateButton != null)
        {
            cancelCreateButton.clicked += OnCancelCreateClicked;
        }

        if (tagField != null)
        {
            tagField.RegisterValueChangedCallback(OnTagFieldValueChanged);
        }
    }

    private void UnregisterCallbacks()
    {
        if (overlayRoot != null)
        {
            overlayRoot.UnregisterCallback<PointerUpEvent>(OnOverlayPointerUp);
        }

        if (panelRoot != null)
        {
            panelRoot.UnregisterCallback<PointerDownEvent>(OnPanelPointerDown);
            panelRoot.UnregisterCallback<PointerUpEvent>(OnPanelPointerUp);
        }

        if (closeButton != null)
        {
            closeButton.clicked -= OnCloseClicked;
        }

        if (refreshButton != null)
        {
            refreshButton.clicked -= OnRefreshClicked;
        }

        if (createToggleButton != null)
        {
            createToggleButton.clicked -= OnCreateToggleClicked;
        }

        if (submitCreateButton != null)
        {
            submitCreateButton.clicked -= OnSubmitCreateClicked;
        }

        if (cancelCreateButton != null)
        {
            cancelCreateButton.clicked -= OnCancelCreateClicked;
        }

        if (tagField != null)
        {
            tagField.UnregisterValueChangedCallback(OnTagFieldValueChanged);
        }
    }

    private void SetVisible(bool isVisible)
    {
        if (overlayRoot == null)
        {
            return;
        }

        overlayRoot.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
        if (isVisible)
        {
            Refresh();
            panelDragController?.CenterInBounds();
        }
        else
        {
            panelDragController?.StopDragging();
            SetCreateFormVisible(false, clearFields: false);
        }
    }

    private void UpdateSummaryLabels()
    {
        if (currentGuildNameLabel != null)
        {
            GuildSummaryResponse currentGuild = FindCurrentGuild();
            currentGuildNameLabel.text = currentGuild != null
                ? BuildGuildDisplayName(currentGuild)
                : "Unaffiliated";
        }

        if (currentGuildMetaLabel != null)
        {
            GuildSummaryResponse currentGuild = FindCurrentGuild();
            currentGuildMetaLabel.text = currentGuild != null
                ? $"Leader: {SanitizeLabel(currentGuild.leaderDisplayName)} | Members: {Mathf.Max(1, currentGuild.memberCount)}"
                : "You are free to found a new guild.";
        }

        if (totalGuildCountLabel != null)
        {
            totalGuildCountLabel.text = cachedGuilds.Length.ToString("N0");
        }

        if (createAvailabilityLabel != null)
        {
            createAvailabilityLabel.text = CanCreateGuild()
                ? "Ready to found a guild"
                : FindCurrentGuild() != null
                    ? "Already sailing under a guild banner"
                    : "Sign in to create guilds";
        }
    }

    private void UpdateButtonStates()
    {
        bool hasSession = BackendSession.IsLoggedIn;
        bool canInteract = hasSession && !isLoadingGuilds && !isCreatingGuild;
        bool canCreateGuild = CanCreateGuild() && canInteract;

        if (refreshButton != null)
        {
            refreshButton.SetEnabled(canInteract);
            refreshButton.text = isLoadingGuilds ? "Refreshing..." : "Refresh List";
        }

        if (createToggleButton != null)
        {
            createToggleButton.SetEnabled(canCreateGuild);
            createToggleButton.text = IsCreateFormVisible() ? "Hide Create Form" : "Create New Guild";
        }

        if (submitCreateButton != null)
        {
            submitCreateButton.SetEnabled(canCreateGuild && IsCreateFormVisible());
            submitCreateButton.text = isCreatingGuild ? "Creating..." : "Found Guild";
        }

        if (cancelCreateButton != null)
        {
            cancelCreateButton.SetEnabled(!isCreatingGuild);
        }

        if (nameField != null)
        {
            nameField.SetEnabled(canCreateGuild);
        }

        if (tagField != null)
        {
            tagField.SetEnabled(canCreateGuild);
        }

        if (descriptionField != null)
        {
            descriptionField.SetEnabled(canCreateGuild);
        }
    }

    private void RebuildGuildList()
    {
        if (guildListScrollView == null)
        {
            return;
        }

        VisualElement contentRoot = guildListScrollView.contentContainer;
        contentRoot.Clear();

        if (cachedGuilds == null || cachedGuilds.Length == 0)
        {
            var emptyLabel = new Label("No guilds have been founded yet.");
            emptyLabel.AddToClassList("guild-empty-label");
            contentRoot.Add(emptyLabel);
            return;
        }

        for (int index = 0; index < cachedGuilds.Length; index++)
        {
            GuildSummaryResponse guild = cachedGuilds[index];
            if (guild == null)
            {
                continue;
            }

            VisualElement card = new VisualElement();
            card.AddToClassList("guild-list-card");
            if (guild.isCurrentPlayerMember)
            {
                card.AddToClassList(CurrentGuildCardClass);
            }

            var titleLabel = new Label(BuildGuildDisplayName(guild));
            titleLabel.AddToClassList("guild-list-title");

            var metaLabel = new Label(
                $"Leader: {SanitizeLabel(guild.leaderDisplayName)} | Members: {Mathf.Max(1, guild.memberCount)} | Founded: {FormatGuildDate(guild.createdAt)}");
            metaLabel.AddToClassList("guild-list-meta");

            var descriptionLabel = new Label(string.IsNullOrWhiteSpace(guild.description)
                ? "No guild description yet."
                : SanitizeLabel(guild.description));
            descriptionLabel.AddToClassList("guild-list-description");

            if (guild.isCurrentPlayerMember)
            {
                var memberBadge = new Label("Your Guild");
                memberBadge.AddToClassList("guild-list-badge");
                card.Add(memberBadge);
            }

            card.Add(titleLabel);
            card.Add(metaLabel);
            card.Add(descriptionLabel);
            contentRoot.Add(card);
        }
    }

    private async Task RefreshGuildsAsync(bool preserveStatusMessage)
    {
        if (!BackendSession.IsLoggedIn)
        {
            SetStatus("Sign in before loading guild data.", isSuccess: false, useTone: true);
            Refresh();
            return;
        }

        CancellationToken cancellationToken = BeginOperation();
        isLoadingGuilds = true;
        UpdateButtonStates();
        if (!preserveStatusMessage)
        {
            SetStatus("Loading guild roster...", isSuccess: true, useTone: false);
        }

        try
        {
            GuildListResponse response = await ExecuteWithPlayerDataRetryAsync(
                (client, accessToken, ct) => client.GetGuildsAsync(accessToken, ct),
                cancellationToken);

            cachedGuilds = response?.guilds ?? Array.Empty<GuildSummaryResponse>();
            currentGuildId = response != null ? response.currentGuildId ?? string.Empty : string.Empty;
            hasLoadedGuilds = true;

            UpdateCurrentGuildIdFromCache();
            NotifyLocalPlayerGuildPrefixRefresh();
            RebuildGuildList();
            UpdateSummaryLabels();
            UpdateButtonStates();

            if (!preserveStatusMessage)
            {
                SetStatus(
                    cachedGuilds.Length == 0
                        ? "No guilds are registered yet."
                        : $"Loaded {cachedGuilds.Length} guild{(cachedGuilds.Length == 1 ? string.Empty : "s")}.",
                    isSuccess: true,
                    useTone: false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isSuccess: false, useTone: true);
        }
        finally
        {
            isLoadingGuilds = false;
            UpdateButtonStates();
        }
    }

    private async Task CreateGuildAsync()
    {
        if (!CanCreateGuild())
        {
            SetStatus("You are already in a guild.", isSuccess: false, useTone: true);
            Refresh();
            return;
        }

        string guildName = nameField != null ? (nameField.value ?? string.Empty).Trim() : string.Empty;
        string guildTag = tagField != null ? (tagField.value ?? string.Empty).Trim().ToUpperInvariant() : string.Empty;
        string guildDescription = descriptionField != null ? (descriptionField.value ?? string.Empty).Trim() : string.Empty;

        string validationMessage = ValidateGuildForm(guildName, guildTag, guildDescription);
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            SetStatus(validationMessage, isSuccess: false, useTone: true);
            return;
        }

        CancellationToken cancellationToken = BeginOperation();
        isCreatingGuild = true;
        UpdateButtonStates();
        SetStatus("Founding guild...", isSuccess: true, useTone: false);

        try
        {
            GuildSummaryResponse createdGuild = await ExecuteWithPlayerDataRetryAsync(
                (client, accessToken, ct) => client.CreateGuildAsync(accessToken, guildName, guildTag, guildDescription, ct),
                cancellationToken);

            currentGuildId = createdGuild != null ? createdGuild.id ?? string.Empty : string.Empty;
            SetCreateFormVisible(false, clearFields: true);
            await RefreshGuildsAsync(preserveStatusMessage: true);
            NotifyLocalPlayerGuildPrefixRefresh();
            SetStatus($"Guild founded: {BuildGuildDisplayName(createdGuild)}", isSuccess: true, useTone: true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isSuccess: false, useTone: true);
        }
        finally
        {
            isCreatingGuild = false;
            UpdateButtonStates();
        }
    }

    private async Task<T> ExecuteWithPlayerDataRetryAsync<T>(
        Func<BackendPlayerDataClient, string, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        Exception lastError = null;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string accessToken = BackendSession.AccessToken;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("Your session is missing an access token. Please sign in again.");
            }

            var playerDataClient = new BackendPlayerDataClient(BackendSession.PlayerDataBaseUrl);
            try
            {
                return await operation(playerDataClient, accessToken, cancellationToken);
            }
            catch (BackendApiException ex) when (ex.StatusCode == 401 && attempt == 0)
            {
                lastError = ex;
                await RefreshAccessTokenAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        throw lastError ?? new InvalidOperationException("Guild request failed.");
    }

    private static async Task RefreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!BackendSession.HasRefreshToken)
        {
            throw new InvalidOperationException("Your session has expired. Please sign in again.");
        }

        var authClient = new BackendAuthClient(BackendSession.AuthBaseUrl);
        TokenResponse refreshed = await authClient.RefreshAsync(BackendSession.RefreshToken, cancellationToken);
        if (refreshed == null || string.IsNullOrWhiteSpace(refreshed.accessToken) || string.IsNullOrWhiteSpace(refreshed.refreshToken))
        {
            throw new InvalidOperationException("Token refresh failed. Please sign in again.");
        }

        BackendSession.SetTokens(refreshed.accessToken, refreshed.refreshToken, refreshed.expiresInSeconds);
    }

    private CancellationToken BeginOperation()
    {
        CancelOperation();
        operationCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
        return operationCts.Token;
    }

    private void CancelOperation()
    {
        if (operationCts == null)
        {
            return;
        }

        if (!operationCts.IsCancellationRequested)
        {
            operationCts.Cancel();
        }

        operationCts.Dispose();
        operationCts = null;
    }

    private void UpdateCurrentGuildIdFromCache()
    {
        if (!string.IsNullOrWhiteSpace(currentGuildId))
        {
            return;
        }

        GuildSummaryResponse currentGuild = FindCurrentGuild();
        currentGuildId = currentGuild != null ? currentGuild.id ?? string.Empty : string.Empty;
    }

    private GuildSummaryResponse FindCurrentGuild()
    {
        if (cachedGuilds == null || cachedGuilds.Length == 0)
        {
            return null;
        }

        for (int index = 0; index < cachedGuilds.Length; index++)
        {
            GuildSummaryResponse guild = cachedGuilds[index];
            if (guild == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(currentGuildId) &&
                string.Equals(guild.id, currentGuildId, StringComparison.OrdinalIgnoreCase))
            {
                return guild;
            }

            if (guild.isCurrentPlayerMember)
            {
                return guild;
            }
        }

        return null;
    }

    private bool CanCreateGuild()
    {
        return BackendSession.IsLoggedIn && string.IsNullOrWhiteSpace(currentGuildId);
    }

    private bool IsCreateFormVisible()
    {
        return createFormRoot != null && createFormRoot.resolvedStyle.display != DisplayStyle.None;
    }

    private void SetCreateFormVisible(bool isVisible, bool clearFields)
    {
        if (createFormRoot != null)
        {
            createFormRoot.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (!isVisible && clearFields)
        {
            ClearCreateFields();
        }

        if (isVisible)
        {
            nameField?.Focus();
        }

        UpdateButtonStates();
    }

    private void ClearCreateFields()
    {
        if (nameField != null)
        {
            nameField.value = string.Empty;
        }

        if (tagField != null)
        {
            tagField.value = string.Empty;
        }

        if (descriptionField != null)
        {
            descriptionField.value = string.Empty;
        }
    }

    private void SetStatus(string message, bool isSuccess, bool useTone)
    {
        if (statusLabel == null)
        {
            return;
        }

        statusLabel.text = string.IsNullOrWhiteSpace(message) ? DefaultStatusMessage : SanitizeLabel(message);
        statusLabel.EnableInClassList(StatusSuccessClass, useTone && isSuccess);
        statusLabel.EnableInClassList(StatusErrorClass, useTone && !isSuccess);
    }

    private void OnRefreshClicked()
    {
        _ = RefreshGuildsAsync(preserveStatusMessage: false);
    }

    private void OnCreateToggleClicked()
    {
        if (!CanCreateGuild())
        {
            SetStatus("You already belong to a guild, so founding a new one is disabled.", isSuccess: false, useTone: true);
            return;
        }

        SetCreateFormVisible(!IsCreateFormVisible(), clearFields: false);
    }

    private void OnSubmitCreateClicked()
    {
        _ = CreateGuildAsync();
    }

    private void OnCancelCreateClicked()
    {
        SetCreateFormVisible(false, clearFields: true);
        SetStatus(DefaultStatusMessage, isSuccess: false, useTone: false);
    }

    private void OnTagFieldValueChanged(ChangeEvent<string> evt)
    {
        if (tagField == null)
        {
            return;
        }

        string normalized = NormalizeGuildTagInput(evt.newValue);
        if (string.Equals(evt.newValue, normalized, StringComparison.Ordinal))
        {
            return;
        }

        tagField.SetValueWithoutNotify(normalized);
    }

    private void OnCloseClicked()
    {
        Hide();
    }

    private static void OnPanelPointerDown(PointerDownEvent evt)
    {
        evt.StopPropagation();
    }

    private static void OnPanelPointerUp(PointerUpEvent evt)
    {
        evt.StopPropagation();
    }

    private void OnOverlayPointerUp(PointerUpEvent evt)
    {
        if (evt.button != (int)MouseButton.LeftMouse || !ReferenceEquals(evt.target, overlayRoot))
        {
            return;
        }

        if (panelDragController != null && panelDragController.IsDragging)
        {
            evt.StopPropagation();
            return;
        }

        Hide();
        evt.StopPropagation();
    }

    private static string ValidateGuildForm(string guildName, string guildTag, string guildDescription)
    {
        if (string.IsNullOrWhiteSpace(guildName))
        {
            return "Guild name is required.";
        }

        if (guildName.Length < 3 || guildName.Length > 32)
        {
            return "Guild name must be between 3 and 32 characters.";
        }

        if (string.IsNullOrWhiteSpace(guildTag))
        {
            return "Guild abbreviation is required.";
        }

        if (guildTag.Length != GuildAbbreviationLength)
        {
            return "Guild abbreviation must be exactly 3 letters.";
        }

        for (int index = 0; index < guildTag.Length; index++)
        {
            if (!char.IsLetter(guildTag[index]))
            {
                return "Guild abbreviation may only contain letters.";
            }
        }

        if (!string.IsNullOrWhiteSpace(guildDescription) && guildDescription.Length > 180)
        {
            return "Guild description must be 180 characters or fewer.";
        }

        return string.Empty;
    }

    private static string BuildGuildDisplayName(GuildSummaryResponse guild)
    {
        if (guild == null)
        {
            return "Unknown Guild";
        }

        string name = SanitizeLabel(guild.name);
        string tag = SanitizeLabel(guild.tag).ToUpperInvariant();
        return string.IsNullOrWhiteSpace(tag) ? name : $"[{tag}] {name}";
    }

    private void NotifyLocalPlayerGuildPrefixRefresh()
    {
        if (Player.LocalPlayer == null || !Player.LocalPlayer.IsOwner || !Player.LocalPlayer.IsSpawned)
        {
            return;
        }

        Player.LocalPlayer.RequestGuildAbbreviationRefresh();
    }

    private static string FormatGuildDate(string rawDate)
    {
        if (DateTimeOffset.TryParse(rawDate, out DateTimeOffset parsed))
        {
            return parsed.ToLocalTime().ToString("yyyy-MM-dd");
        }

        return "Unknown";
    }

    private static string SanitizeLabel(string value)
    {
        return UiTextSanitizer.SanitizeForLabel(value ?? string.Empty, collapseWhitespace: true);
    }

    private static string NormalizeGuildTagInput(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[GuildAbbreviationLength];
        int writeIndex = 0;
        for (int index = 0; index < value.Length && writeIndex < GuildAbbreviationLength; index++)
        {
            char current = value[index];
            if (!char.IsLetter(current))
            {
                continue;
            }

            buffer[writeIndex] = char.ToUpperInvariant(current);
            writeIndex++;
        }

        return writeIndex == 0 ? string.Empty : new string(buffer[..writeIndex]);
    }
}
