using System;
using System.Collections.Generic;
using GameSystem;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class SettingsMenuController : IDisposable
{
    private const string SharedPanelStyleResourcePath = "Shared/OverlayPanel";
    private const string UxmlResourcePath = "Settings/SettingsMenu";
    private const string UssResourcePath = "Settings/SettingsMenu";
    private const string DefaultStatusMessage = "Adjust display and gameplay preferences for this sailing session.";
    private const string DisplayCategoryId = "display";
    private const string GameplayCategoryId = "gameplay";
    private const string SessionCategoryId = "session";
    private const string CategoryButtonClass = "window-category-button";
    private const string CategoryButtonSelectedClass = "window-category-button-selected";
    private const string StatusSuccessClass = "settings-status-success";
    private const string StatusErrorClass = "settings-status-error";

    private static readonly List<string> QualityChoices = new List<string> { "Low", "Medium", "High" };
    private static readonly List<string> FramerateChoices = new List<string> { "30 FPS", "60 FPS", "120 FPS" };
    private static readonly List<string> RenderResolutionChoices = new List<string> { "Native", "1440p", "1080p", "720p" };
    private static readonly List<string> SpeedFormatChoices = new List<string> { "MPH", "KPH" };

    private readonly VisualElement attachTarget;
    private readonly Action logoutAction;

    private VisualElement overlayRoot;
    private VisualElement panelRoot;
    private VisualElement headerRoot;
    private VisualElement categoryList;
    private ScrollView contentScrollView;
    private Label categoryTitleLabel;
    private Label categorySubtitleLabel;
    private Label statusLabel;
    private Button closeButton;
    private VisualElement displaySectionRoot;
    private VisualElement gameplaySectionRoot;
    private VisualElement sessionSectionRoot;
    private DropdownField qualityDropdown;
    private DropdownField framerateDropdown;
    private DropdownField renderResolutionDropdown;
    private Toggle dynamicResolutionToggle;
    private Toggle srpBatcherToggle;
    private DropdownField speedFormatDropdown;
    private Button returnToSeaButton;
    private Button signOutButton;
    private DraggableWindowController panelDragController;

    private string selectedCategoryId = DisplayCategoryId;
    private bool suppressCallbacks;

    public SettingsMenuController(VisualElement attachTarget, Action logoutAction)
    {
        this.attachTarget = attachTarget;
        this.logoutAction = logoutAction;
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
            Debug.LogWarning($"SettingsMenuController: Missing UXML resource '{UxmlResourcePath}'.");
            return;
        }

        TemplateContainer container = visualTree.Instantiate();
        overlayRoot = container.Q<VisualElement>("SettingsMenuOverlay") ?? container;
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
        ConfigureChoices();
        panelDragController = new DraggableWindowController(overlayRoot, panelRoot, headerRoot, closeButton);
        RegisterCallbacks();
        BuildCategories();
        Refresh();
        SetStatus(DefaultStatusMessage, isSuccess: false, useTone: false);
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

        AppSettings appSettings = AppSettings.Instance;
        suppressCallbacks = true;
        try
        {
            if (qualityDropdown != null)
            {
                qualityDropdown.SetValueWithoutNotify(GetCurrentQualityLabel());
            }

            if (framerateDropdown != null)
            {
                framerateDropdown.SetValueWithoutNotify(appSettings != null
                    ? GetFramerateLabel(appSettings.targetFramerate)
                    : FramerateChoices[1]);
            }

            if (renderResolutionDropdown != null)
            {
                renderResolutionDropdown.SetValueWithoutNotify(appSettings != null
                    ? GetRenderResolutionLabel(appSettings.maxRenderSize)
                    : RenderResolutionChoices[0]);
            }

            if (dynamicResolutionToggle != null)
            {
                dynamicResolutionToggle.SetValueWithoutNotify(appSettings != null && appSettings.variableResolution);
            }

            if (srpBatcherToggle != null)
            {
                srpBatcherToggle.SetValueWithoutNotify(appSettings != null && appSettings.IsSrpBatcherEnabled);
            }

            if (speedFormatDropdown != null)
            {
                speedFormatDropdown.SetValueWithoutNotify(appSettings != null
                    ? GetSpeedFormatLabel(appSettings.speedFormat)
                    : SpeedFormatChoices[0]);
            }
        }
        finally
        {
            suppressCallbacks = false;
        }

        UpdateControlStates(appSettings != null);
        RefreshVisibleSection(resetScroll: false);
    }

    public void Dispose()
    {
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
        categoryList = null;
        contentScrollView = null;
        categoryTitleLabel = null;
        categorySubtitleLabel = null;
        statusLabel = null;
        closeButton = null;
        displaySectionRoot = null;
        gameplaySectionRoot = null;
        sessionSectionRoot = null;
        qualityDropdown = null;
        framerateDropdown = null;
        renderResolutionDropdown = null;
        dynamicResolutionToggle = null;
        srpBatcherToggle = null;
        speedFormatDropdown = null;
        returnToSeaButton = null;
        signOutButton = null;
        selectedCategoryId = DisplayCategoryId;
        suppressCallbacks = false;
    }

    private void BindUiElements()
    {
        if (overlayRoot == null)
        {
            return;
        }

        panelRoot = overlayRoot.Q<VisualElement>("SettingsMenuPanel");
        headerRoot = overlayRoot.Q<VisualElement>("SettingsMenuHeader");
        categoryList = overlayRoot.Q<VisualElement>("SettingsCategoryList");
        contentScrollView = overlayRoot.Q<ScrollView>("SettingsContentScrollView");
        categoryTitleLabel = overlayRoot.Q<Label>("SettingsCategoryTitleLabel");
        categorySubtitleLabel = overlayRoot.Q<Label>("SettingsCategorySubtitleLabel");
        statusLabel = overlayRoot.Q<Label>("SettingsStatusLabel");
        closeButton = overlayRoot.Q<Button>("SettingsMenuCloseButton");
        displaySectionRoot = overlayRoot.Q<VisualElement>("SettingsDisplaySection");
        gameplaySectionRoot = overlayRoot.Q<VisualElement>("SettingsGameplaySection");
        sessionSectionRoot = overlayRoot.Q<VisualElement>("SettingsSessionSection");
        qualityDropdown = overlayRoot.Q<DropdownField>("SettingsQualityDropdown");
        framerateDropdown = overlayRoot.Q<DropdownField>("SettingsFramerateDropdown");
        renderResolutionDropdown = overlayRoot.Q<DropdownField>("SettingsRenderResolutionDropdown");
        dynamicResolutionToggle = overlayRoot.Q<Toggle>("SettingsDynamicResolutionToggle");
        srpBatcherToggle = overlayRoot.Q<Toggle>("SettingsSrpBatcherToggle");
        speedFormatDropdown = overlayRoot.Q<DropdownField>("SettingsSpeedFormatDropdown");
        returnToSeaButton = overlayRoot.Q<Button>("SettingsReturnToSeaButton");
        signOutButton = overlayRoot.Q<Button>("SettingsSignOutButton");
    }

    private void ConfigureChoices()
    {
        if (qualityDropdown != null)
        {
            qualityDropdown.choices = QualityChoices;
        }

        if (framerateDropdown != null)
        {
            framerateDropdown.choices = FramerateChoices;
        }

        if (renderResolutionDropdown != null)
        {
            renderResolutionDropdown.choices = RenderResolutionChoices;
        }

        if (speedFormatDropdown != null)
        {
            speedFormatDropdown.choices = SpeedFormatChoices;
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

        if (qualityDropdown != null)
        {
            qualityDropdown.RegisterValueChangedCallback(OnQualityChanged);
        }

        if (framerateDropdown != null)
        {
            framerateDropdown.RegisterValueChangedCallback(OnFramerateChanged);
        }

        if (renderResolutionDropdown != null)
        {
            renderResolutionDropdown.RegisterValueChangedCallback(OnRenderResolutionChanged);
        }

        if (dynamicResolutionToggle != null)
        {
            dynamicResolutionToggle.RegisterValueChangedCallback(OnDynamicResolutionChanged);
        }

        if (srpBatcherToggle != null)
        {
            srpBatcherToggle.RegisterValueChangedCallback(OnSrpBatcherChanged);
        }

        if (speedFormatDropdown != null)
        {
            speedFormatDropdown.RegisterValueChangedCallback(OnSpeedFormatChanged);
        }

        if (returnToSeaButton != null)
        {
            returnToSeaButton.clicked += OnReturnToSeaClicked;
        }

        if (signOutButton != null)
        {
            signOutButton.clicked += OnSignOutClicked;
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

        if (qualityDropdown != null)
        {
            qualityDropdown.UnregisterValueChangedCallback(OnQualityChanged);
        }

        if (framerateDropdown != null)
        {
            framerateDropdown.UnregisterValueChangedCallback(OnFramerateChanged);
        }

        if (renderResolutionDropdown != null)
        {
            renderResolutionDropdown.UnregisterValueChangedCallback(OnRenderResolutionChanged);
        }

        if (dynamicResolutionToggle != null)
        {
            dynamicResolutionToggle.UnregisterValueChangedCallback(OnDynamicResolutionChanged);
        }

        if (srpBatcherToggle != null)
        {
            srpBatcherToggle.UnregisterValueChangedCallback(OnSrpBatcherChanged);
        }

        if (speedFormatDropdown != null)
        {
            speedFormatDropdown.UnregisterValueChangedCallback(OnSpeedFormatChanged);
        }

        if (returnToSeaButton != null)
        {
            returnToSeaButton.clicked -= OnReturnToSeaClicked;
        }

        if (signOutButton != null)
        {
            signOutButton.clicked -= OnSignOutClicked;
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
            panelDragController?.CenterInBounds();
        }
        else
        {
            panelDragController?.StopDragging();
        }
    }

    private void BuildCategories()
    {
        if (categoryList == null)
        {
            return;
        }

        categoryList.Clear();
        AddCategoryButton(DisplayCategoryId, "Display");
        AddCategoryButton(GameplayCategoryId, "Gameplay");
        AddCategoryButton(SessionCategoryId, "Session");
        RefreshVisibleSection(resetScroll: true);
    }

    private void AddCategoryButton(string categoryId, string title)
    {
        Button categoryButton = new Button(() =>
        {
            selectedCategoryId = categoryId;
            BuildCategories();
        })
        {
            text = title
        };

        categoryButton.AddToClassList(CategoryButtonClass);
        if (string.Equals(selectedCategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
        {
            categoryButton.AddToClassList(CategoryButtonSelectedClass);
        }

        categoryList.Add(categoryButton);
    }

    private void RefreshVisibleSection(bool resetScroll)
    {
        if (displaySectionRoot != null)
        {
            displaySectionRoot.style.display = string.Equals(selectedCategoryId, DisplayCategoryId, StringComparison.OrdinalIgnoreCase)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        if (gameplaySectionRoot != null)
        {
            gameplaySectionRoot.style.display = string.Equals(selectedCategoryId, GameplayCategoryId, StringComparison.OrdinalIgnoreCase)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        if (sessionSectionRoot != null)
        {
            sessionSectionRoot.style.display = string.Equals(selectedCategoryId, SessionCategoryId, StringComparison.OrdinalIgnoreCase)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        if (categoryTitleLabel != null)
        {
            categoryTitleLabel.text = GetCategoryTitle(selectedCategoryId);
        }

        if (categorySubtitleLabel != null)
        {
            categorySubtitleLabel.text = GetCategorySubtitle(selectedCategoryId);
        }

        if (resetScroll && contentScrollView != null)
        {
            contentScrollView.scrollOffset = Vector2.zero;
        }
    }

    private void UpdateControlStates(bool hasAppSettings)
    {
        qualityDropdown?.SetEnabled(hasAppSettings);
        framerateDropdown?.SetEnabled(hasAppSettings);
        renderResolutionDropdown?.SetEnabled(hasAppSettings);
        dynamicResolutionToggle?.SetEnabled(hasAppSettings);
        srpBatcherToggle?.SetEnabled(hasAppSettings);
        speedFormatDropdown?.SetEnabled(hasAppSettings);
        returnToSeaButton?.SetEnabled(true);
        signOutButton?.SetEnabled(true);
    }

    private void SetStatus(string message, bool isSuccess, bool useTone)
    {
        if (statusLabel == null)
        {
            return;
        }

        statusLabel.text = string.IsNullOrWhiteSpace(message) ? DefaultStatusMessage : message;
        statusLabel.EnableInClassList(StatusSuccessClass, useTone && isSuccess);
        statusLabel.EnableInClassList(StatusErrorClass, useTone && !isSuccess);
    }

    private void OnQualityChanged(ChangeEvent<string> evt)
    {
        if (suppressCallbacks)
        {
            return;
        }

        if (!TryResolveQualityLevel(evt.newValue, out int qualityLevel))
        {
            SetStatus($"'{evt.newValue}' is not a valid quality preset.", isSuccess: false, useTone: true);
            Refresh();
            return;
        }

        QualitySettings.SetQualityLevel(qualityLevel, true);
        SetStatus($"Graphics quality set to {evt.newValue}.", isSuccess: true, useTone: true);
    }

    private void OnFramerateChanged(ChangeEvent<string> evt)
    {
        if (suppressCallbacks)
        {
            return;
        }

        AppSettings appSettings = AppSettings.Instance;
        if (appSettings == null)
        {
            SetStatus("App settings are not available right now.", isSuccess: false, useTone: true);
            Refresh();
            return;
        }

        appSettings.SetTargetFramerate(ParseFramerate(evt.newValue));
        SetStatus($"Target frame rate set to {evt.newValue}.", isSuccess: true, useTone: true);
    }

    private void OnRenderResolutionChanged(ChangeEvent<string> evt)
    {
        if (suppressCallbacks)
        {
            return;
        }

        AppSettings appSettings = AppSettings.Instance;
        if (appSettings == null)
        {
            SetStatus("App settings are not available right now.", isSuccess: false, useTone: true);
            Refresh();
            return;
        }

        appSettings.SetRenderResolution(ParseRenderResolution(evt.newValue));
        SetStatus($"Render size cap updated to {evt.newValue}.", isSuccess: true, useTone: true);
    }

    private void OnDynamicResolutionChanged(ChangeEvent<bool> evt)
    {
        if (suppressCallbacks)
        {
            return;
        }

        AppSettings appSettings = AppSettings.Instance;
        if (appSettings == null)
        {
            SetStatus("App settings are not available right now.", isSuccess: false, useTone: true);
            Refresh();
            return;
        }

        appSettings.SetDynamicResolutionEnabled(evt.newValue);
        SetStatus(
            evt.newValue ? "Dynamic resolution enabled." : "Dynamic resolution disabled.",
            isSuccess: true,
            useTone: true);
    }

    private void OnSrpBatcherChanged(ChangeEvent<bool> evt)
    {
        if (suppressCallbacks)
        {
            return;
        }

        AppSettings appSettings = AppSettings.Instance;
        if (appSettings == null)
        {
            SetStatus("App settings are not available right now.", isSuccess: false, useTone: true);
            Refresh();
            return;
        }

        appSettings.ToggleSRPBatcher(evt.newValue);
        SetStatus(
            evt.newValue ? "SRP batcher enabled." : "SRP batcher disabled.",
            isSuccess: true,
            useTone: true);
    }

    private void OnSpeedFormatChanged(ChangeEvent<string> evt)
    {
        if (suppressCallbacks)
        {
            return;
        }

        AppSettings appSettings = AppSettings.Instance;
        if (appSettings == null)
        {
            SetStatus("App settings are not available right now.", isSuccess: false, useTone: true);
            Refresh();
            return;
        }

        appSettings.SetSpeedFormat(ParseSpeedFormat(evt.newValue));
        SetStatus($"Speed readouts now use {evt.newValue}.", isSuccess: true, useTone: true);
    }

    private void OnReturnToSeaClicked()
    {
        Hide();
    }

    private void OnSignOutClicked()
    {
        Hide();
        logoutAction?.Invoke();
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

    private static string GetCurrentQualityLabel()
    {
        int currentLevel = QualitySettings.GetQualityLevel();
        string[] names = QualitySettings.names;
        if (currentLevel >= 0 && currentLevel < names.Length)
        {
            string currentName = names[currentLevel];
            for (int index = 0; index < QualityChoices.Count; index++)
            {
                if (string.Equals(QualityChoices[index], currentName, StringComparison.OrdinalIgnoreCase))
                {
                    return QualityChoices[index];
                }
            }
        }

        return QualityChoices[Mathf.Clamp(currentLevel, 0, QualityChoices.Count - 1)];
    }

    private static bool TryResolveQualityLevel(string label, out int qualityLevel)
    {
        string[] names = QualitySettings.names;
        for (int index = 0; index < names.Length; index++)
        {
            if (string.Equals(names[index], label, StringComparison.OrdinalIgnoreCase))
            {
                qualityLevel = index;
                return true;
            }
        }

        qualityLevel = -1;
        return false;
    }

    private static string GetFramerateLabel(AppSettings.Framerate framerate)
    {
        return framerate switch
        {
            AppSettings.Framerate._30 => FramerateChoices[0],
            AppSettings.Framerate._60 => FramerateChoices[1],
            AppSettings.Framerate._120 => FramerateChoices[2],
            _ => FramerateChoices[1]
        };
    }

    private static AppSettings.Framerate ParseFramerate(string label)
    {
        return label switch
        {
            "30 FPS" => AppSettings.Framerate._30,
            "120 FPS" => AppSettings.Framerate._120,
            _ => AppSettings.Framerate._60
        };
    }

    private static string GetRenderResolutionLabel(AppSettings.RenderRes renderResolution)
    {
        return renderResolution switch
        {
            AppSettings.RenderRes._1440p => RenderResolutionChoices[1],
            AppSettings.RenderRes._1080p => RenderResolutionChoices[2],
            AppSettings.RenderRes._720p => RenderResolutionChoices[3],
            _ => RenderResolutionChoices[0]
        };
    }

    private static AppSettings.RenderRes ParseRenderResolution(string label)
    {
        return label switch
        {
            "1440p" => AppSettings.RenderRes._1440p,
            "1080p" => AppSettings.RenderRes._1080p,
            "720p" => AppSettings.RenderRes._720p,
            _ => AppSettings.RenderRes._Native
        };
    }

    private static string GetSpeedFormatLabel(AppSettings.SpeedFormat format)
    {
        return format switch
        {
            AppSettings.SpeedFormat._Kph => SpeedFormatChoices[1],
            _ => SpeedFormatChoices[0]
        };
    }

    private static AppSettings.SpeedFormat ParseSpeedFormat(string label)
    {
        return string.Equals(label, "KPH", StringComparison.OrdinalIgnoreCase)
            ? AppSettings.SpeedFormat._Kph
            : AppSettings.SpeedFormat._Mph;
    }

    private static string GetCategoryTitle(string categoryId)
    {
        if (string.Equals(categoryId, GameplayCategoryId, StringComparison.OrdinalIgnoreCase))
        {
            return "Gameplay";
        }

        if (string.Equals(categoryId, SessionCategoryId, StringComparison.OrdinalIgnoreCase))
        {
            return "Session";
        }

        return "Display";
    }

    private static string GetCategorySubtitle(string categoryId)
    {
        if (string.Equals(categoryId, GameplayCategoryId, StringComparison.OrdinalIgnoreCase))
        {
            return "Pick how ship speed and navigation info should be presented during play.";
        }

        if (string.Equals(categoryId, SessionCategoryId, StringComparison.OrdinalIgnoreCase))
        {
            return "Return to the HUD or sign out when you are done adjusting settings.";
        }

        return "Tune rendering quality, frame rate, and performance controls for this client.";
    }
}
