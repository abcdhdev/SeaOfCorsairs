using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class MarketController : IDisposable
{
    private const string SharedPanelStyleResourcePath = "Shared/OverlayPanel";
    private const string MarketUxmlResourcePath = "Market/Market";
    private const string MarketItemRowUxmlResourcePath = "Market/MarketItemRow";
    private const string MarketStyleResourcePath = "Market/Market";
    private const string MarketCategoryCannonsId = "cannons";
    private const string MarketCategoryShipsId = "ships";
    private const string MarketCategoryAmmoId = MarketInventoryCatalogRuntime.AmmoCategoryId;
    private const string MarketCategoryHarpoonsId = MarketInventoryCatalogRuntime.HarpoonsCategoryId;
    private const string MarketCategoryActionItemsId = MarketInventoryCatalogRuntime.ActionItemsCategoryId;
    private const string DefaultStatusMessage = "Select an item to buy it from the market.";
    private const string CategoryButtonClass = "window-category-button";
    private const string CategoryButtonSelectedClass = "window-category-button-selected";
    private const string OwnedStateOwnedClass = "market-owned-state-owned";
    private const string OwnedStateLockedClass = "market-owned-state-locked";
    private const string StatusSuccessClass = "market-status-success";
    private const string StatusErrorClass = "market-status-error";
    private const string BuyButtonUnaffordableClass = "market-buy-button-unaffordable";
    private const int MarketRowHeight = 144;

    private readonly VisualElement attachTarget;
    private readonly Func<Player> localPlayerProvider;
    private readonly List<MarketItemViewModel> visibleItems = new List<MarketItemViewModel>();
    private readonly Dictionary<string, int> inventoryAmounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> ownedShipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private VisualElement overlayRoot;
    private VisualElement panelRoot;
    private VisualElement headerRoot;
    private VisualElement categoryList;
    private ListView itemListView;
    private Label categoryTitleLabel;
    private Button closeButton;
    private Label goldValueLabel;
    private Label diamondValueLabel;
    private Label statusLabel;
    private VisualTreeAsset itemRowTemplate;
    private DraggableWindowController panelDragController;
    private Player observedPlayer;

    private string selectedCategoryId = MarketCategoryCannonsId;
    private string pendingPurchaseKey = string.Empty;
    private int displayedGold = -1;
    private int displayedDiamonds = -1;
    private string displayedInventorySnapshot = string.Empty;
    private string displayedOwnedShipsCsv = string.Empty;

    public MarketController(VisualElement attachTarget, Func<Player> localPlayerProvider)
    {
        this.attachTarget = attachTarget;
        this.localPlayerProvider = localPlayerProvider;
    }

    public bool IsVisible => overlayRoot != null && overlayRoot.resolvedStyle.display != DisplayStyle.None;

    public VisualElement OverlayRoot => overlayRoot;

    public void Attach()
    {
        if (attachTarget == null || overlayRoot != null)
        {
            return;
        }

        VisualTreeAsset marketTree = Resources.Load<VisualTreeAsset>(MarketUxmlResourcePath);
        if (marketTree == null)
        {
            Debug.LogWarning($"MarketController: Missing UXML resource '{MarketUxmlResourcePath}'.");
            return;
        }

        TemplateContainer marketContainer = marketTree.Instantiate();
        overlayRoot = marketContainer.Q<VisualElement>("MarketOverlay") ?? marketContainer;
        if (!ReferenceEquals(overlayRoot, marketContainer))
        {
            overlayRoot.RemoveFromHierarchy();
        }

        overlayRoot.pickingMode = PickingMode.Position;

        StyleSheet sharedPanelStyle = Resources.Load<StyleSheet>(SharedPanelStyleResourcePath);
        if (sharedPanelStyle != null)
        {
            overlayRoot.styleSheets.Add(sharedPanelStyle);
        }

        StyleSheet marketStyle = Resources.Load<StyleSheet>(MarketStyleResourcePath);
        if (marketStyle != null)
        {
            overlayRoot.styleSheets.Add(marketStyle);
        }

        attachTarget.Add(overlayRoot);
        overlayRoot.BlockRaycasts();

        itemRowTemplate = Resources.Load<VisualTreeAsset>(MarketItemRowUxmlResourcePath);
        if (itemRowTemplate == null)
        {
            Debug.LogWarning($"MarketController: Missing item row UXML resource '{MarketItemRowUxmlResourcePath}'.");
        }

        BindUiElements();
        ConfigureListView();
        panelDragController = new DraggableWindowController(overlayRoot, panelRoot, headerRoot, closeButton);
        RegisterCallbacks();
        BuildCategories();
        RebuildItems();
        Refresh();
        SetStatus(DefaultStatusMessage, isSuccess: false, useTone: false, clearPendingPurchase: false);
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

        Player localPlayer = GetValidLocalPlayer();
        if (localPlayer == null && !string.IsNullOrWhiteSpace(pendingPurchaseKey))
        {
            pendingPurchaseKey = string.Empty;
        }

        TrackObservedPlayer(localPlayer);

        int currentGold = localPlayer != null ? Mathf.Max(0, localPlayer.Gold) : 0;
        int currentDiamonds = localPlayer != null ? Mathf.Max(0, localPlayer.Diamonds) : 0;
        string inventorySnapshot = localPlayer != null ? localPlayer.InventorySnapshot ?? string.Empty : string.Empty;
        string ownedShipsCsv = localPlayer != null ? localPlayer.OwnedShipIdsCsv ?? string.Empty : string.Empty;

        if (goldValueLabel != null && displayedGold != currentGold)
        {
            goldValueLabel.text = currentGold.ToString("N0");
        }

        if (diamondValueLabel != null && displayedDiamonds != currentDiamonds)
        {
            diamondValueLabel.text = currentDiamonds.ToString("N0");
        }

        bool stateChanged = displayedGold != currentGold ||
                            displayedDiamonds != currentDiamonds ||
                            !string.Equals(displayedInventorySnapshot, inventorySnapshot, StringComparison.Ordinal) ||
                            !string.Equals(displayedOwnedShipsCsv, ownedShipsCsv, StringComparison.Ordinal);

        displayedGold = currentGold;
        displayedDiamonds = currentDiamonds;
        displayedInventorySnapshot = inventorySnapshot;
        displayedOwnedShipsCsv = ownedShipsCsv;

        if (!stateChanged)
        {
            return;
        }

        SyncInventoryAmounts(localPlayer);
        SyncOwnedSet(ownedShipsCsv, ownedShipIds, MarketShipCatalogRuntime.NormalizeShipId);
        RefreshListItems();
    }

    public void Dispose()
    {
        if (overlayRoot == null)
        {
            return;
        }

        panelDragController?.Dispose();
        panelDragController = null;
        UntrackObservedPlayer();
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
        itemListView = null;
        categoryTitleLabel = null;
        closeButton = null;
        goldValueLabel = null;
        diamondValueLabel = null;
        statusLabel = null;
        itemRowTemplate = null;
        pendingPurchaseKey = string.Empty;
        selectedCategoryId = MarketCategoryCannonsId;
        displayedGold = -1;
        displayedDiamonds = -1;
        displayedInventorySnapshot = string.Empty;
        displayedOwnedShipsCsv = string.Empty;
        inventoryAmounts.Clear();
        ownedShipIds.Clear();
        visibleItems.Clear();
    }

    private void BindUiElements()
    {
        if (overlayRoot == null)
        {
            return;
        }

        panelRoot = overlayRoot.Q<VisualElement>("MarketPanel");
        headerRoot = overlayRoot.Q<VisualElement>("MarketHeader");
        categoryList = overlayRoot.Q<VisualElement>("MarketCategoryList");
        itemListView = overlayRoot.Q<ListView>("MarketItemsListView");
        categoryTitleLabel = overlayRoot.Q<Label>("MarketCategoryTitleLabel");
        closeButton = overlayRoot.Q<Button>("MarketCloseButton");
        goldValueLabel = overlayRoot.Q<Label>("MarketGoldValueLabel");
        diamondValueLabel = overlayRoot.Q<Label>("MarketDiamondValueLabel");
        statusLabel = overlayRoot.Q<Label>("MarketStatusLabel");
    }

    private void ConfigureListView()
    {
        if (itemListView == null || itemRowTemplate == null)
        {
            return;
        }

        itemListView.selectionType = SelectionType.None;
        itemListView.fixedItemHeight = MarketRowHeight;
        itemListView.reorderable = false;
        itemListView.makeItem = MakeMarketItem;
        itemListView.bindItem = BindMarketItem;
        itemListView.itemsSource = visibleItems;
    }

    private VisualElement MakeMarketItem()
    {
        TemplateContainer rowTemplate = itemRowTemplate.Instantiate();
        VisualElement row = rowTemplate.Q<VisualElement>("MarketItemRow") ?? rowTemplate;
        if (!ReferenceEquals(row, rowTemplate))
        {
            row.RemoveFromHierarchy();
        }

        row.userData = new MarketRowController(row, OnBuyClicked);
        return row;
    }

    private void BindMarketItem(VisualElement item, int index)
    {
        if (item == null || index < 0 || index >= visibleItems.Count)
        {
            return;
        }

        MarketItemViewModel definition = visibleItems[index];
        MarketRowController controller = item.userData as MarketRowController;
        int ownedAmount = GetOwnedAmount(definition);
        controller?.Bind(
            definition,
            displayedGold,
            displayedDiamonds,
            ownedAmount,
            definition.IsUniquePurchase && IsOwned(definition),
            string.Equals(pendingPurchaseKey, definition.PurchaseKey, StringComparison.OrdinalIgnoreCase));
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

    private void Show()
    {
        SetVisible(true);
        BuildCategories();
        RebuildItems();
        displayedGold = -1;
        displayedDiamonds = -1;
        displayedInventorySnapshot = string.Empty;
        displayedOwnedShipsCsv = string.Empty;
        Refresh();
        SetStatus(DefaultStatusMessage, isSuccess: false, useTone: false, clearPendingPurchase: false);
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
        AddCategoryButton(MarketCategoryCannonsId, "Cannons");
        AddCategoryButton(MarketCategoryShipsId, "Ships");
        AddCategoryButton(MarketCategoryAmmoId, "Ammo");
        AddCategoryButton(MarketCategoryHarpoonsId, "Harpoons");
        AddCategoryButton(MarketCategoryActionItemsId, "Action Items");

        if (categoryTitleLabel != null)
        {
            categoryTitleLabel.text = GetCategoryTitle(selectedCategoryId);
        }
    }

    private void AddCategoryButton(string categoryId, string title)
    {
        Button categoryButton = new Button(() =>
        {
            selectedCategoryId = categoryId;
            BuildCategories();
            RebuildItems();
            Refresh();
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

    private void RebuildItems()
    {
        visibleItems.Clear();

        if (string.Equals(selectedCategoryId, MarketCategoryCannonsId, StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<MarketCannonData> catalogCannons = MarketCannonCatalogRuntime.GetCannons();
            for (int index = 0; index < catalogCannons.Count; index++)
            {
                MarketCannonData cannon = catalogCannons[index];
                if (cannon == null)
                {
                    continue;
                }

                visibleItems.Add(MarketItemViewModel.FromCannon(cannon));
            }
        }
        else if (string.Equals(selectedCategoryId, MarketCategoryShipsId, StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<MarketShipData> catalogShips = MarketShipCatalogRuntime.GetShips();
            for (int index = 0; index < catalogShips.Count; index++)
            {
                MarketShipData ship = catalogShips[index];
                if (ship == null)
                {
                    continue;
                }

                visibleItems.Add(MarketItemViewModel.FromShip(ship));
            }
        }
        else
        {
            IReadOnlyList<MarketInventoryItemData> catalogItems = MarketInventoryCatalogRuntime.GetItemsForCategory(selectedCategoryId);
            for (int index = 0; index < catalogItems.Count; index++)
            {
                MarketInventoryItemData item = catalogItems[index];
                if (item == null)
                {
                    continue;
                }

                visibleItems.Add(MarketItemViewModel.FromInventoryItem(item));
            }
        }

        visibleItems.Sort(static (left, right) =>
        {
            int sortOrderComparison = left.SortOrder.CompareTo(right.SortOrder);
            if (sortOrderComparison != 0)
            {
                return sortOrderComparison;
            }

            return string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
        });

        if (categoryTitleLabel != null)
        {
            categoryTitleLabel.text = GetCategoryTitle(selectedCategoryId);
        }

        RefreshListItems(rebuild: true);
    }

    private void RefreshListItems(bool rebuild = false)
    {
        if (itemListView == null)
        {
            return;
        }

        itemListView.itemsSource = visibleItems;
        if (rebuild)
        {
            itemListView.Rebuild();
            return;
        }

        itemListView.RefreshItems();
    }

    private void TrackObservedPlayer(Player localPlayer)
    {
        if (ReferenceEquals(observedPlayer, localPlayer))
        {
            return;
        }

        UntrackObservedPlayer();
        observedPlayer = localPlayer;

        if (observedPlayer != null)
        {
            observedPlayer.OnCannonPurchaseResult += OnObservedPlayerCannonPurchaseResult;
            observedPlayer.OnShipPurchaseResult += OnObservedPlayerShipPurchaseResult;
            observedPlayer.OnInventoryItemPurchaseResult += OnObservedPlayerInventoryItemPurchaseResult;
            observedPlayer.OnInventoryChanged += OnObservedPlayerInventoryChanged;
        }
    }

    private void UntrackObservedPlayer()
    {
        if (observedPlayer != null)
        {
            observedPlayer.OnCannonPurchaseResult -= OnObservedPlayerCannonPurchaseResult;
            observedPlayer.OnShipPurchaseResult -= OnObservedPlayerShipPurchaseResult;
            observedPlayer.OnInventoryItemPurchaseResult -= OnObservedPlayerInventoryItemPurchaseResult;
            observedPlayer.OnInventoryChanged -= OnObservedPlayerInventoryChanged;
            observedPlayer = null;
        }
    }

    private void OnObservedPlayerCannonPurchaseResult(string cannonId, bool success, string message)
    {
        string purchaseKey = BuildPurchaseKey(MarketItemCategory.Cannon, NormalizeCannonId(cannonId));
        if (string.Equals(pendingPurchaseKey, purchaseKey, StringComparison.OrdinalIgnoreCase))
        {
            pendingPurchaseKey = string.Empty;
        }

        SetStatus(message, success, useTone: true, clearPendingPurchase: success);
        RefreshListItems();
        Refresh();
    }

    private void OnObservedPlayerShipPurchaseResult(string shipId, bool success, string message)
    {
        string purchaseKey = BuildPurchaseKey(MarketItemCategory.Ship, MarketShipCatalogRuntime.NormalizeShipId(shipId));
        if (string.Equals(pendingPurchaseKey, purchaseKey, StringComparison.OrdinalIgnoreCase))
        {
            pendingPurchaseKey = string.Empty;
        }

        SetStatus(message, success, useTone: true, clearPendingPurchase: success);
        RefreshListItems();
        Refresh();
    }

    private void OnObservedPlayerInventoryItemPurchaseResult(string itemId, bool success, string message)
    {
        string purchaseKey = BuildPurchaseKey(MarketItemCategory.Inventory, PlayerInventoryState.NormalizeItemId(itemId));
        if (string.Equals(pendingPurchaseKey, purchaseKey, StringComparison.OrdinalIgnoreCase))
        {
            pendingPurchaseKey = string.Empty;
        }

        SetStatus(message, success, useTone: true, clearPendingPurchase: success);
        RefreshListItems();
        Refresh();
    }

    private void OnObservedPlayerInventoryChanged()
    {
        Refresh();
    }

    private void OnBuyClicked(MarketItemViewModel definition)
    {
        if (definition == null)
        {
            return;
        }

        Player localPlayer = GetValidLocalPlayer();
        if (localPlayer == null)
        {
            SetStatus("Your player is not ready yet.", isSuccess: false, useTone: true);
            return;
        }

        if (definition.IsUniquePurchase && IsOwned(definition))
        {
            SetStatus($"{definition.DisplayName} is already owned.", isSuccess: false, useTone: true);
            return;
        }

        if (!definition.Cost.CanAfford(localPlayer.Gold, localPlayer.Diamonds))
        {
            SetStatus($"{definition.Cost.BuildShortageText(localPlayer.Gold, localPlayer.Diamonds)} for {definition.DisplayName}.", isSuccess: false, useTone: true);
            return;
        }

        bool requestSent = definition.Category switch
        {
            MarketItemCategory.Cannon => localPlayer.RequestCannonPurchase(definition.Id),
            MarketItemCategory.Ship => localPlayer.RequestShipPurchase(definition.Id),
            MarketItemCategory.Inventory => localPlayer.RequestInventoryItemPurchase(definition.Id),
            _ => false
        };

        if (!requestSent)
        {
            SetStatus("Could not send the purchase request to the server.", isSuccess: false, useTone: true);
            return;
        }

        pendingPurchaseKey = definition.PurchaseKey;
        SetStatus($"Purchasing {definition.DisplayName}...", isSuccess: true, useTone: false, clearPendingPurchase: false);
        RefreshListItems();
    }

    private bool IsOwned(MarketItemViewModel definition)
    {
        if (definition == null)
        {
            return false;
        }

        return definition.Category == MarketItemCategory.Cannon
            ? false
            : definition.Category == MarketItemCategory.Ship && ownedShipIds.Contains(definition.Id);
    }

    private void SetStatus(string message, bool isSuccess, bool useTone, bool clearPendingPurchase = true)
    {
        if (clearPendingPurchase && isSuccess)
        {
            pendingPurchaseKey = string.Empty;
        }

        if (statusLabel == null)
        {
            return;
        }

        statusLabel.text = string.IsNullOrWhiteSpace(message) ? DefaultStatusMessage : message;
        statusLabel.EnableInClassList(StatusSuccessClass, useTone && isSuccess);
        statusLabel.EnableInClassList(StatusErrorClass, useTone && !isSuccess);
    }

    private Player GetValidLocalPlayer()
    {
        Player localPlayer = localPlayerProvider != null ? localPlayerProvider.Invoke() : null;
        if (localPlayer == null || !localPlayer.IsOwner || !localPlayer.IsSpawned)
        {
            return null;
        }

        return localPlayer;
    }

    private void OnCloseClicked()
    {
        Hide();
    }

    private void OnOverlayPointerUp(PointerUpEvent evt)
    {
        if (evt.button != (int)MouseButton.LeftMouse || !ReferenceEquals(evt.target, overlayRoot))
        {
            return;
        }

        if (panelDragController != null && panelDragController.IsDragging)
        {
            return;
        }

        Hide();
        evt.StopPropagation();
    }

    private static void OnPanelPointerDown(PointerDownEvent evt)
    {
        evt.StopPropagation();
    }

    private static void OnPanelPointerUp(PointerUpEvent evt)
    {
        evt.StopPropagation();
    }

    private static void SyncOwnedSet(string ownedCsv, HashSet<string> targetSet, Func<string, string> normalizer)
    {
        targetSet.Clear();
        if (string.IsNullOrWhiteSpace(ownedCsv))
        {
            return;
        }

        string[] splitValues = ownedCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < splitValues.Length; index++)
        {
            string normalizedId = normalizer != null ? normalizer.Invoke(splitValues[index]) : splitValues[index];
            if (string.IsNullOrWhiteSpace(normalizedId))
            {
                continue;
            }

            targetSet.Add(normalizedId);
        }
    }

    private void SyncInventoryAmounts(Player localPlayer)
    {
        inventoryAmounts.Clear();
        if (localPlayer == null)
        {
            return;
        }

        IReadOnlyList<PlayerInventoryItemState> inventoryItems = localPlayer.GetInventoryItems();
        Dictionary<string, int> amounts = PlayerInventoryState.CreateAmountLookup(inventoryItems);
        foreach (KeyValuePair<string, int> entry in amounts)
        {
            inventoryAmounts[entry.Key] = Mathf.Max(0, entry.Value);
        }
    }

    private int GetOwnedAmount(MarketItemViewModel definition)
    {
        if (definition == null)
        {
            return 0;
        }

        if (definition.Category == MarketItemCategory.Ship)
        {
            return ownedShipIds.Contains(definition.Id) ? 1 : 0;
        }

        return inventoryAmounts.TryGetValue(definition.Id, out int ownedAmount)
            ? Mathf.Max(0, ownedAmount)
            : 0;
    }

    private static string NormalizeCannonId(string cannonId)
    {
        return string.IsNullOrWhiteSpace(cannonId)
            ? string.Empty
            : cannonId.Trim().ToLowerInvariant();
    }

    private static string BuildPurchaseKey(MarketItemCategory category, string itemId)
    {
        return $"{(int)category}:{itemId ?? string.Empty}";
    }

    private static string GetCategoryTitle(string categoryId)
    {
        if (string.Equals(categoryId, MarketCategoryShipsId, StringComparison.OrdinalIgnoreCase))
        {
            return "Ships";
        }

        if (string.Equals(categoryId, MarketCategoryAmmoId, StringComparison.OrdinalIgnoreCase))
        {
            return "Ammo";
        }

        if (string.Equals(categoryId, MarketCategoryHarpoonsId, StringComparison.OrdinalIgnoreCase))
        {
            return "Harpoons";
        }

        if (string.Equals(categoryId, MarketCategoryActionItemsId, StringComparison.OrdinalIgnoreCase))
        {
            return "Action Items";
        }

        return "Cannons";
    }

    private enum MarketItemCategory
    {
        Cannon = 0,
        Ship = 1,
        Inventory = 2
    }

    private sealed class MarketItemViewModel
    {
        private MarketItemViewModel(
            MarketItemCategory category,
            string id,
            string displayName,
            string description,
            Texture2D icon,
            string statLine1,
            string statLine2,
            string statLine3,
            MarketCost cost,
            bool isUniquePurchase,
            int purchaseAmount,
            int sortOrder)
        {
            Category = category;
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Description = description ?? string.Empty;
            Icon = icon;
            StatLine1 = statLine1 ?? string.Empty;
            StatLine2 = statLine2 ?? string.Empty;
            StatLine3 = statLine3 ?? string.Empty;
            Cost = cost ?? new MarketCost();
            IsUniquePurchase = isUniquePurchase;
            PurchaseAmount = Mathf.Max(1, purchaseAmount);
            SortOrder = Mathf.Max(0, sortOrder);
        }

        public MarketItemCategory Category { get; }
        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public Texture2D Icon { get; }
        public string StatLine1 { get; }
        public string StatLine2 { get; }
        public string StatLine3 { get; }
        public MarketCost Cost { get; }
        public bool IsUniquePurchase { get; }
        public int PurchaseAmount { get; }
        public int SortOrder { get; }
        public string PurchaseKey => BuildPurchaseKey(Category, Id);

        public static MarketItemViewModel FromCannon(MarketCannonData cannon)
        {
            bool hasAdvancedCombatStats =
                cannon.CriticalHitProbability > 0f ||
                cannon.CriticalHitDamage > 0f ||
                cannon.BonusDamageFlat > 0 ||
                cannon.BonusDamagePercentage > 0f;

            return new MarketItemViewModel(
                MarketItemCategory.Cannon,
                cannon.Id,
                cannon.DisplayName,
                cannon.Description,
                cannon.Icon,
                hasAdvancedCombatStats
                    ? $"Hit {cannon.HitProbability}% | Crit {cannon.CriticalHitProbability:0.#}%"
                    : $"Hit {cannon.HitProbability}%",
                hasAdvancedCombatStats
                    ? $"Range {cannon.CannonRange:0.#} | Reload {cannon.ReloadTimeSeconds:0.#}s"
                    : $"Range {cannon.CannonRange:0.#}",
                hasAdvancedCombatStats
                    ? BuildCannonBonusStatLine(cannon)
                    : $"Reload {cannon.ReloadTimeSeconds:0.#}s",
                cannon.Cost,
                false,
                1,
                cannon.SortOrder);
        }

        private static string BuildCannonBonusStatLine(MarketCannonData cannon)
        {
            var parts = new List<string>();
            if (cannon.CriticalHitDamage > 0f)
            {
                parts.Add($"Crit Dmg +{cannon.CriticalHitDamage:0.#}%");
            }

            if (cannon.BonusDamageFlat > 0)
            {
                parts.Add($"+{cannon.BonusDamageFlat} Dmg");
            }

            if (cannon.BonusDamagePercentage > 0f)
            {
                parts.Add($"+{cannon.BonusDamagePercentage:0.#}% Dmg");
            }

            return parts.Count > 0 ? string.Join(" | ", parts) : string.Empty;
        }

        public static MarketItemViewModel FromShip(MarketShipData ship)
        {
            return new MarketItemViewModel(
                MarketItemCategory.Ship,
                ship.Id,
                ship.DisplayName,
                ship.Description,
                ship.Icon,
                ship.PrimaryStatLabel,
                ship.SecondaryStatLabel,
                ship.TertiaryStatLabel,
                ship.Cost,
                true,
                1,
                ship.SortOrder);
        }

        public static MarketItemViewModel FromInventoryItem(MarketInventoryItemData item)
        {
            return new MarketItemViewModel(
                MarketItemCategory.Inventory,
                item.Id,
                item.DisplayName,
                item.Description,
                item.Icon,
                item.StatLine1,
                item.StatLine2,
                item.StatLine3,
                item.Cost,
                false,
                item.PurchaseAmount,
                item.SortOrder);
        }
    }

    private sealed class MarketRowController
    {
        private readonly VisualElement imageElement;
        private readonly Label nameLabel;
        private readonly Label descriptionLabel;
        private readonly Label statLine1Label;
        private readonly Label statLine2Label;
        private readonly Label statLine3Label;
        private readonly Label ownedLabel;
        private readonly VisualElement costListElement;
        private readonly Button buyButton;
        private readonly Action<MarketItemViewModel> buyAction;

        private MarketItemViewModel boundDefinition;

        public MarketRowController(VisualElement root, Action<MarketItemViewModel> buyAction)
        {
            imageElement = root.Q<VisualElement>("MarketItemThumb");
            nameLabel = root.Q<Label>("MarketItemNameLabel");
            descriptionLabel = root.Q<Label>("MarketItemDescriptionLabel");
            statLine1Label = root.Q<Label>("MarketItemHitProbabilityLabel");
            statLine2Label = root.Q<Label>("MarketItemRangeLabel");
            statLine3Label = root.Q<Label>("MarketItemReloadLabel");
            ownedLabel = root.Q<Label>("MarketItemOwnedLabel");
            costListElement = root.Q<VisualElement>("MarketItemCostList");
            buyButton = root.Q<Button>("MarketItemBuyButton");
            this.buyAction = buyAction;

            if (buyButton != null)
            {
                buyButton.clicked += OnBuyButtonClicked;
            }
        }

        public void Bind(MarketItemViewModel definition, int gold, int diamonds, int ownedAmount, bool isUniqueOwned, bool isPending)
        {
            boundDefinition = definition;

            if (imageElement != null)
            {
                imageElement.style.backgroundImage = definition != null && definition.Icon != null
                    ? new StyleBackground(definition.Icon)
                    : new StyleBackground();
            }

            if (definition == null)
            {
                return;
            }

            if (nameLabel != null)
            {
                nameLabel.text = definition.DisplayName;
            }

            if (descriptionLabel != null)
            {
                descriptionLabel.text = definition.Description;
            }

            BindStatLabel(statLine1Label, definition.StatLine1);
            BindStatLabel(statLine2Label, definition.StatLine2);
            BindStatLabel(statLine3Label, definition.StatLine3);

            RebuildCostList(definition.Cost);

            bool canAfford = definition.Cost.CanAfford(gold, diamonds);
            if (ownedLabel != null)
            {
                bool hasAnyOwned = ownedAmount > 0;
                ownedLabel.EnableInClassList(OwnedStateOwnedClass, hasAnyOwned);
                ownedLabel.EnableInClassList(OwnedStateLockedClass, !hasAnyOwned);
                ownedLabel.text = definition.IsUniquePurchase
                    ? isUniqueOwned
                        ? "Owned"
                        : canAfford
                            ? "Ready to buy"
                            : definition.Cost.BuildShortageText(gold, diamonds)
                    : $"Owned: {Mathf.Max(0, ownedAmount):N0}";
            }

            if (buyButton != null)
            {
                bool isDisabledByOwnership = definition.IsUniquePurchase && isUniqueOwned;
                buyButton.text = isDisabledByOwnership
                    ? "Owned"
                    : isPending
                        ? "Buying..."
                        : definition.PurchaseAmount > 1
                            ? $"Buy {definition.PurchaseAmount:N0}"
                            : "Buy 1";
                buyButton.SetEnabled(!isDisabledByOwnership && !isPending);
                buyButton.EnableInClassList(BuyButtonUnaffordableClass, !isDisabledByOwnership && !isPending && !canAfford);
            }
        }

        private static void BindStatLabel(Label label, string text)
        {
            if (label == null)
            {
                return;
            }

            bool hasText = !string.IsNullOrWhiteSpace(text);
            label.text = hasText ? text : string.Empty;
            label.style.display = hasText ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RebuildCostList(MarketCost cost)
        {
            if (costListElement == null)
            {
                return;
            }

            costListElement.Clear();

            if (cost == null || !cost.HasEntries)
            {
                AddCostChip(MarketCurrencyType.Gold, "Owned");
                return;
            }

            IReadOnlyList<MarketCostEntry> entries = cost.Entries;
            for (int index = 0; index < entries.Count; index++)
            {
                MarketCostEntry entry = entries[index];
                if (entry == null || entry.Amount <= 0)
                {
                    continue;
                }

                AddCostChip(entry.CurrencyType, entry.Amount.ToString("N0"));
            }
        }

        private void AddCostChip(MarketCurrencyType currencyType, string displayValue)
        {
            if (costListElement == null)
            {
                return;
            }

            var chip = new VisualElement();
            chip.AddToClassList("market-cost-chip");

            var icon = new VisualElement();
            icon.AddToClassList("market-cost-icon");
            icon.AddToClassList(currencyType == MarketCurrencyType.Diamonds ? "market-cost-icon-diamond" : "market-cost-icon-gold");

            var valueLabel = new Label(displayValue);
            valueLabel.AddToClassList("market-cost-value");

            chip.Add(icon);
            chip.Add(valueLabel);
            costListElement.Add(chip);
        }

        private void OnBuyButtonClicked()
        {
            if (boundDefinition == null)
            {
                return;
            }

            buyAction?.Invoke(boundDefinition);
        }
    }
}
