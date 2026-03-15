using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class ShipSectionController : IDisposable
{
    private const string SharedPanelStyleResourcePath = "Shared/OverlayPanel";
    private const string UxmlResourcePath = "ShipSection/ShipSection";
    private const string UssResourcePath = "ShipSection/ShipSection";
    private const string ItemRowUxmlResourcePath = "ShipSection/ShipSectionItemRow";
    private const string TabButtonClass = "window-tab-button";
    private const string TabButtonSelectedClass = "window-tab-button-selected";
    private const string CategoryButtonClass = "window-category-button";
    private const string CategoryButtonSelectedClass = "window-category-button-selected";
    private const string EmptyStateClass = "window-empty-state";

    private readonly VisualElement attachTarget;
    private readonly ShipSectionData data;

    private VisualElement overlayRoot;
    private VisualElement panelRoot;
    private VisualElement headerRoot;
    private Label titleLabel;
    private Label subtitleLabel;
    private VisualElement tabStrip;
    private VisualElement categoryList;
    private VisualElement itemList;
    private Button closeButton;
    private VisualTreeAsset itemRowTemplate;
    private DraggableWindowController panelDragController;

    private string selectedTabId;
    private string selectedCategoryId;

    public ShipSectionController(VisualElement attachTarget, ShipSectionData data)
    {
        this.attachTarget = attachTarget;
        this.data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public bool IsVisible => overlayRoot != null && overlayRoot.resolvedStyle.display != DisplayStyle.None;

    public void Attach()
    {
        if (attachTarget == null || overlayRoot != null)
        {
            return;
        }

        VisualTreeAsset visualTree = Resources.Load<VisualTreeAsset>(UxmlResourcePath);
        if (visualTree == null)
        {
            Debug.LogWarning($"ShipSectionController: Missing UXML resource '{UxmlResourcePath}'.");
            return;
        }

        TemplateContainer container = visualTree.Instantiate();
        overlayRoot = container.Q<VisualElement>("ShipSectionOverlayRoot") ?? container;
        if (!ReferenceEquals(overlayRoot, container))
        {
            overlayRoot.RemoveFromHierarchy();
        }
        overlayRoot.pickingMode = PickingMode.Position;

        StyleSheet sharedPanelStyleSheet = Resources.Load<StyleSheet>(SharedPanelStyleResourcePath);
        if (sharedPanelStyleSheet != null)
        {
            overlayRoot.styleSheets.Add(sharedPanelStyleSheet);
        }

        StyleSheet screenStyleSheet = Resources.Load<StyleSheet>(UssResourcePath);
        if (screenStyleSheet != null)
        {
            overlayRoot.styleSheets.Add(screenStyleSheet);
        }

        attachTarget.Add(overlayRoot);
        overlayRoot.BlockRaycasts();

        itemRowTemplate = Resources.Load<VisualTreeAsset>(ItemRowUxmlResourcePath);

        BindUiElements();
        panelDragController = new DraggableWindowController(overlayRoot, panelRoot, headerRoot, closeButton);
        RegisterCallbacks();
        EnsureDefaultSelection();
        RefreshView();
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

        SetVisible(!IsVisible);
    }

    public void Hide()
    {
        SetVisible(false);
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
        titleLabel = null;
        subtitleLabel = null;
        tabStrip = null;
        categoryList = null;
        itemList = null;
        closeButton = null;
        itemRowTemplate = null;
    }

    private void BindUiElements()
    {
        if (overlayRoot == null)
        {
            return;
        }

        panelRoot = overlayRoot.Q<VisualElement>("ShipSectionPanel");
        headerRoot = overlayRoot.Q<VisualElement>("ShipSectionHeader");
        titleLabel = overlayRoot.Q<Label>("ShipSectionTitleLabel");
        subtitleLabel = overlayRoot.Q<Label>("ShipSectionSubtitleLabel");
        tabStrip = overlayRoot.Q<VisualElement>("ShipSectionTabStrip");
        categoryList = overlayRoot.Q<VisualElement>("ShipSectionCategoryList");
        itemList = overlayRoot.Q<VisualElement>("ShipSectionItemsList");
        closeButton = overlayRoot.Q<Button>("ShipSectionCloseButton");
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
    }

    private void EnsureDefaultSelection()
    {
        if (data.Tabs.Count == 0)
        {
            selectedTabId = string.Empty;
            selectedCategoryId = string.Empty;
            return;
        }

        if (string.IsNullOrEmpty(selectedTabId))
        {
            selectedTabId = data.Tabs[0].Id;
        }

        EnsureCategorySelectionForTab(selectedTabId);
    }

    private void EnsureCategorySelectionForTab(string tabId)
    {
        if (string.IsNullOrEmpty(tabId))
        {
            selectedCategoryId = string.Empty;
            return;
        }

        if (IsCategoryInTab(selectedCategoryId, tabId))
        {
            return;
        }

        selectedCategoryId = string.Empty;
        for (int i = 0; i < data.Categories.Count; i++)
        {
            ShipSectionCategoryData category = data.Categories[i];
            if (string.Equals(category.TabId, tabId, StringComparison.Ordinal))
            {
                selectedCategoryId = category.Id;
                return;
            }
        }
    }

    private void RefreshView()
    {
        if (overlayRoot == null)
        {
            return;
        }

        if (titleLabel != null)
        {
            titleLabel.text = data.Title;
        }

        if (subtitleLabel != null)
        {
            string tabTitle = GetTabTitle(selectedTabId);
            string categoryTitle = GetCategoryTitle(selectedCategoryId);
            subtitleLabel.text = string.IsNullOrEmpty(tabTitle)
                ? data.Subtitle
                : $"{data.Subtitle} Active: {tabTitle}{(string.IsNullOrEmpty(categoryTitle) ? string.Empty : $" / {categoryTitle}")}";
        }

        RefreshTabs();
        RefreshCategories();
        RefreshItems();
    }

    private void RefreshTabs()
    {
        if (tabStrip == null)
        {
            return;
        }

        tabStrip.Clear();

        for (int i = 0; i < data.Tabs.Count; i++)
        {
            ShipSectionTabData tab = data.Tabs[i];
            Button button = new Button(() =>
            {
                selectedTabId = tab.Id;
                EnsureCategorySelectionForTab(selectedTabId);
                RefreshView();
            })
            {
                text = tab.Title
            };

            button.AddToClassList(TabButtonClass);
            if (string.Equals(tab.Id, selectedTabId, StringComparison.Ordinal))
            {
                button.AddToClassList(TabButtonSelectedClass);
            }

            tabStrip.Add(button);
        }
    }

    private void RefreshCategories()
    {
        if (categoryList == null)
        {
            return;
        }

        categoryList.Clear();

        for (int i = 0; i < data.Categories.Count; i++)
        {
            ShipSectionCategoryData category = data.Categories[i];
            if (!string.Equals(category.TabId, selectedTabId, StringComparison.Ordinal))
            {
                continue;
            }

            Button button = new Button(() =>
            {
                selectedCategoryId = category.Id;
                RefreshView();
            })
            {
                text = category.Title
            };

            button.AddToClassList(CategoryButtonClass);
            if (string.Equals(category.Id, selectedCategoryId, StringComparison.Ordinal))
            {
                button.AddToClassList(CategoryButtonSelectedClass);
            }

            categoryList.Add(button);
        }
    }

    private void RefreshItems()
    {
        if (itemList == null)
        {
            return;
        }

        itemList.Clear();

        bool hasItems = false;
        for (int i = 0; i < data.Items.Count; i++)
        {
            ShipSectionItemData item = data.Items[i];
            if (!MatchesSelection(item))
            {
                continue;
            }

            hasItems = true;
            itemList.Add(CreateItemRow(item));
        }

        if (!hasItems)
        {
            Label emptyState = new Label("No dummy items are configured for this category yet.");
            emptyState.AddToClassList(EmptyStateClass);
            itemList.Add(emptyState);
        }
    }

    private VisualElement CreateItemRow(ShipSectionItemData item)
    {
        VisualElement row = CreateItemRowFromTemplate();
        Label thumbLabel = row.Q<Label>("ShipSectionItemThumbLabel");
        Label nameLabel = row.Q<Label>("ShipSectionItemNameLabel");
        Label descriptionLabel = row.Q<Label>("ShipSectionItemDescriptionLabel");
        Label priceLabel = row.Q<Label>("ShipSectionItemPriceLabel");
        Label quantityLabel = row.Q<Label>("ShipSectionItemQuantityLabel");
        Button minusButton = row.Q<Button>("ShipSectionItemMinusButton");
        Button plusButton = row.Q<Button>("ShipSectionItemPlusButton");
        VisualElement thumb = row.Q<VisualElement>("ShipSectionItemThumb");

        if (thumbLabel != null)
        {
            thumbLabel.text = BuildThumbLabel(item.Name);
        }

        if (nameLabel != null)
        {
            nameLabel.text = item.Name;
        }

        if (descriptionLabel != null)
        {
            descriptionLabel.text = item.Description;
        }

        if (priceLabel != null)
        {
            priceLabel.text = item.Cost.ToString();
        }

        if (quantityLabel != null)
        {
            quantityLabel.text = item.Quantity.ToString();
        }

        if (thumb != null)
        {
            thumb.style.backgroundColor = new StyleColor(ParseColor(item.AccentColor, new Color(0.41f, 0.73f, 0.88f, 1f)));
        }

        if (minusButton != null && quantityLabel != null)
        {
            minusButton.clicked += () =>
            {
                item.ChangeQuantity(-1);
                quantityLabel.text = item.Quantity.ToString();
            };
        }

        if (plusButton != null && quantityLabel != null)
        {
            plusButton.clicked += () =>
            {
                item.ChangeQuantity(1);
                quantityLabel.text = item.Quantity.ToString();
            };
        }

        return row;
    }

    private VisualElement CreateItemRowFromTemplate()
    {
        if (itemRowTemplate == null)
        {
            return new VisualElement();
        }

        TemplateContainer container = itemRowTemplate.Instantiate();
        VisualElement row = container.Q<VisualElement>("ShipSectionItemRow") ?? container;
        if (!ReferenceEquals(row, container))
        {
            row.RemoveFromHierarchy();
        }

        return row;
    }

    private bool MatchesSelection(ShipSectionItemData item)
    {
        if (item == null)
        {
            return false;
        }

        if (!string.Equals(item.TabId, selectedTabId, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrEmpty(selectedCategoryId))
        {
            return true;
        }

        return string.Equals(item.CategoryId, selectedCategoryId, StringComparison.Ordinal);
    }

    private bool IsCategoryInTab(string categoryId, string tabId)
    {
        if (string.IsNullOrEmpty(categoryId) || string.IsNullOrEmpty(tabId))
        {
            return false;
        }

        for (int i = 0; i < data.Categories.Count; i++)
        {
            ShipSectionCategoryData category = data.Categories[i];
            if (string.Equals(category.Id, categoryId, StringComparison.Ordinal) &&
                string.Equals(category.TabId, tabId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private string GetTabTitle(string tabId)
    {
        for (int i = 0; i < data.Tabs.Count; i++)
        {
            if (string.Equals(data.Tabs[i].Id, tabId, StringComparison.Ordinal))
            {
                return data.Tabs[i].Title;
            }
        }

        return string.Empty;
    }

    private string GetCategoryTitle(string categoryId)
    {
        for (int i = 0; i < data.Categories.Count; i++)
        {
            if (string.Equals(data.Categories[i].Id, categoryId, StringComparison.Ordinal))
            {
                return data.Categories[i].Title;
            }
        }

        return string.Empty;
    }

    private static string BuildThumbLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "?";
        }

        string[] parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
        }

        return value.Length > 1
            ? value.Substring(0, 2).ToUpperInvariant()
            : value.ToUpperInvariant();
    }

    private static Color ParseColor(string htmlColor, Color fallback)
    {
        if (!string.IsNullOrWhiteSpace(htmlColor) && ColorUtility.TryParseHtmlString(htmlColor, out Color parsedColor))
        {
            return parsedColor;
        }

        return fallback;
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

    private void OnCloseClicked()
    {
        Hide();
    }

    private void OnOverlayPointerUp(PointerUpEvent evt)
    {
        if ((panelDragController != null && panelDragController.IsDragging) || !ReferenceEquals(evt.target, overlayRoot))
        {
            return;
        }

        if (evt.button != (int)MouseButton.LeftMouse)
        {
            return;
        }

        Hide();
        evt.StopPropagation();
    }

    private void OnPanelPointerDown(PointerDownEvent evt)
    {
        evt.StopPropagation();
    }

    private void OnPanelPointerUp(PointerUpEvent evt)
    {
        evt.StopPropagation();
    }
}
