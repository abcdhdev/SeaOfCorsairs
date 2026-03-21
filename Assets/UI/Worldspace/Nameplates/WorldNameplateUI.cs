using System;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
public class WorldNameplateUI : MonoBehaviour
{
    private const string DefaultPanelSettingsResourcePath = "Worldspace/WorldNameplatePanelSettings";
    private const string DefaultVisualTreeResourcePath = "Worldspace/WorldNameplate";
    private const string DefaultStyleSheetResourcePath = "Worldspace/WorldNameplate";
    private const float NameplateScreenVerticalOffset = -0.2f;
    private const float HealthBarAnchorVerticalOffset = 0.75f;
    private const string NameRowElementName = "NameRow";
    private const string GuildPrefixLabelElementName = "GuildPrefixLabel";
    private const string LabelElementName = "EntityNameLabel";
    private const string HealthBarDamageTrailElementName = "HealthBarDamageTrail";
    private const string HealthBarFillElementName = "HealthBarFill";
    private const string HealthBarSliceElementName = "HealthBarSlice";
    private const string CloneSuffix = "(Clone)";
    private const float HealthFillAnimationSpeed = 6.5f;
    private const float DamageTrailShrinkDelay = 0.08f;
    private const float DamageTrailShrinkSpeed = 1.8f;
    private const float SliceAnimationDuration = 0.24f;

    [Header("Assets")]
    [SerializeField] private PanelSettings panelSettings;
    [SerializeField] private VisualTreeAsset visualTreeAsset;
    [SerializeField] private StyleSheet styleSheet;

    [Header("Nameplate")]
    [SerializeField] private bool showNameplate = true;
    [SerializeField, Min(0f)] private float maxRenderDistance = 300f;
    [SerializeField] private bool hideWhenOffscreen = true;
    [SerializeField, Min(0f)] private float offscreenPadding = 0.08f;
    [SerializeField] private string displayNameOverride;
    [SerializeField] private string guildAbbreviationOverride;

    [Header("Health Bar Anchor")]
    [SerializeField] private bool placeUnderTarget = false;
    [SerializeField] private Vector3 worldOffset = new(0f, 0f, -5.9f);
    [SerializeField] private bool hideWhenEmpty = true;
    [SerializeField, Min(0f)] private float cameraDepthBias = 0.35f;

    [Header("World Space")]
    [SerializeField] private UIDocument.WorldSpaceSizeMode worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Dynamic;
    [SerializeField] private Vector2 worldSpaceSize = new Vector2(18f, 4f);

    private IHealthSystem healthSystem;
    private Transform trackedTransform;
    private Vector3 localAnchorOffset;
    private bool hasAnchorOffset;
    private bool anchorUsesRendererBounds;

    private Camera cachedCamera;
    private UIDocument worldSpaceDocument;
    private VisualElement nameRow;
    private Label guildPrefixLabel;
    private Label nameLabel;
    private VisualElement healthBarDamageTrail;
    private VisualElement healthBarFill;
    private VisualElement healthBarSlice;
    private int displayedHealth = -1;
    private int displayedMaxHealth = -1;
    private float displayedHealthPercent = -1f;
    private float targetHealthPercent = 1f;
    private float animatedHealthPercent = 1f;
    private float damageTrailPercent = 1f;
    private float damageTrailDelayRemaining;
    private float sliceAnimationElapsed = SliceAnimationDuration;
    private float sliceAnimationStartPercent = 1f;
    private float sliceAnimationEndPercent = 1f;
    private bool hasAnimatedHealthState;
    private bool styleAttached;
    private bool missingAssetsLogged;
    private bool isVisible;

    public void ApplySettings(
        bool shouldShowNameplate,
        float newMaxRenderDistance,
        bool newPlaceUnderTarget,
        Vector3 newWorldOffset,
        bool newHideWhenEmpty)
    {
        showNameplate = shouldShowNameplate;
        maxRenderDistance = Mathf.Max(0f, newMaxRenderDistance);
        placeUnderTarget = newPlaceUnderTarget;
        worldOffset = newWorldOffset;
        hideWhenEmpty = newHideWhenEmpty;
        hasAnchorOffset = false;
        anchorUsesRendererBounds = false;

        if (!showNameplate)
        {
            SetVisible(false);
        }
    }

    public void SetMaxRenderDistance(float value)
    {
        maxRenderDistance = Mathf.Max(0f, value);
    }

    public void SetShowNameplate(bool value)
    {
        showNameplate = value;
        if (!showNameplate)
        {
            SetVisible(false);
        }
    }

    public void SetAnchorPlacement(bool shouldPlaceUnderTarget)
    {
        placeUnderTarget = shouldPlaceUnderTarget;
        hasAnchorOffset = false;
        anchorUsesRendererBounds = false;
    }

    public void SetWorldOffset(Vector3 newWorldOffset)
    {
        worldOffset = newWorldOffset;
    }

    public void SetHideWhenEmpty(bool value)
    {
        hideWhenEmpty = value;
    }

    public void SetDisplayNameOverride(string value)
    {
        displayNameOverride = value;
        RefreshDisplayName();
    }

    public void SetGuildPrefixOverride(string value)
    {
        guildAbbreviationOverride = NormalizeGuildAbbreviation(value);
        RefreshDisplayName();
    }

    private void OnValidate()
    {
        maxRenderDistance = Mathf.Max(0f, maxRenderDistance);
        offscreenPadding = Mathf.Max(0f, offscreenPadding);
        cameraDepthBias = Mathf.Max(0f, cameraDepthBias);
        hasAnchorOffset = false;
        anchorUsesRendererBounds = false;
    }

    private void Awake()
    {
        ResolveHealthSystem();
    }

    private void OnEnable()
    {
        if (!EnsureDocument())
        {
            return;
        }

        RefreshDisplayName();
        SetVisible(showNameplate);
    }

    private void OnDisable()
    {
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (worldSpaceDocument != null)
        {
            Destroy(worldSpaceDocument.gameObject);
            worldSpaceDocument = null;
        }
    }

    private void LateUpdate()
    {
        if (!showNameplate)
        {
            SetVisible(false);
            return;
        }

        if (!EnsureDocument())
        {
            return;
        }

        if (!TryGetCamera(out Camera renderCamera))
        {
            SetVisible(false);
            return;
        }

        if (healthSystem == null)
        {
            ResolveHealthSystem();
            if (healthSystem == null)
            {
                SetVisible(false);
                return;
            }
        }

        int maxHealth = Mathf.Max(healthSystem.MaxHealth, 1);
        int currentHealth = Mathf.Clamp(healthSystem.CurrentHealth, 0, maxHealth);
        float healthPercent = currentHealth / (float)maxHealth;

        if (hideWhenEmpty && healthPercent <= 0f)
        {
            SetVisible(false);
            return;
        }

        if (trackedTransform == null)
        {
            trackedTransform = transform;
        }

        if (!hasAnchorOffset || !anchorUsesRendererBounds)
        {
            RebuildAnchorOffset();
        }

        Vector3 healthBarPosition = trackedTransform.TransformPoint(localAnchorOffset) + worldOffset;
        if (maxRenderDistance > 0f)
        {
            float maxDistanceSqr = maxRenderDistance * maxRenderDistance;
            if ((renderCamera.transform.position - healthBarPosition).sqrMagnitude > maxDistanceSqr)
            {
                SetVisible(false);
                return;
            }
        }

        Vector3 labelPosition = healthBarPosition + (renderCamera.transform.up * NameplateScreenVerticalOffset);
        if (cameraDepthBias > 0f)
        {
            // Pull the panel slightly toward the camera so water/depth surfaces do not bury it
            // without changing the anchored world-space placement.
            labelPosition -= renderCamera.transform.forward * cameraDepthBias;
        }

        if (hideWhenOffscreen)
        {
            Vector3 viewport = renderCamera.WorldToViewportPoint(labelPosition);
            Vector3 targetViewport = renderCamera.WorldToViewportPoint(trackedTransform.position);
            bool anchorBehindCamera = viewport.z <= 0f;
            bool targetBehindCamera = targetViewport.z <= 0f;
            if ((anchorBehindCamera && targetBehindCamera) ||
                (IsOutsideViewport(viewport, offscreenPadding) && IsOutsideViewport(targetViewport, offscreenPadding)))
            {
                SetVisible(false);
                return;
            }
        }

        Transform documentTransform = worldSpaceDocument.transform;
        documentTransform.SetPositionAndRotation(labelPosition, renderCamera.transform.rotation);

        if (nameLabel == null)
        {
            CacheUiElements();
            RefreshDisplayName();
        }

        UpdateHealthDisplay(currentHealth, maxHealth, healthPercent, Time.unscaledDeltaTime);
        SetVisible(true);
    }

    private bool EnsureDocument()
    {
        if (worldSpaceDocument != null)
        {
            return true;
        }

        if (healthSystem == null)
        {
            ResolveHealthSystem();
            if (healthSystem == null)
            {
                if (!missingAssetsLogged)
                {
                    Debug.LogWarning($"WorldNameplateUI on {gameObject.name} requires IHealthSystem.", this);
                    missingAssetsLogged = true;
                }

                SetVisible(false);
                return false;
            }
        }

        if (panelSettings == null)
        {
            panelSettings = Resources.Load<PanelSettings>(DefaultPanelSettingsResourcePath);
        }

        if (visualTreeAsset == null)
        {
            visualTreeAsset = Resources.Load<VisualTreeAsset>(DefaultVisualTreeResourcePath);
        }

        if (styleSheet == null)
        {
            styleSheet = Resources.Load<StyleSheet>(DefaultStyleSheetResourcePath);
        }

        if (panelSettings == null || visualTreeAsset == null)
        {
            if (!missingAssetsLogged)
            {
                Debug.LogWarning(
                    $"WorldNameplateUI on {gameObject.name} could not load required UITK resources. " +
                    "Expected Resources/Worldspace/WorldNameplatePanelSettings and Resources/Worldspace/WorldNameplate.",
                    this);
                missingAssetsLogged = true;
            }

            SetVisible(false);
            return false;
        }

        GameObject documentObject = new GameObject($"{name} World Nameplate")
        {
            hideFlags = HideFlags.DontSave
        };

        worldSpaceDocument = documentObject.AddComponent<UIDocument>();
        worldSpaceDocument.panelSettings = panelSettings;
        worldSpaceDocument.visualTreeAsset = visualTreeAsset;
        worldSpaceDocument.worldSpaceSizeMode = worldSpaceSizeMode;
        worldSpaceDocument.sortingOrder = 100;

        if (worldSpaceSizeMode == UIDocument.WorldSpaceSizeMode.Fixed)
        {
            worldSpaceDocument.worldSpaceSize = worldSpaceSize;
        }

        CacheUiElements();
        RefreshDisplayName();
        hasAnimatedHealthState = false;
        UpdateHealthDisplay(0, 1, 1f, 0f);
        SetVisible(showNameplate);
        return true;
    }

    private void CacheUiElements()
    {
        if (worldSpaceDocument == null)
        {
            return;
        }

        VisualElement root = worldSpaceDocument.rootVisualElement;
        if (root == null)
        {
            return;
        }

        root.pickingMode = PickingMode.Ignore;

        if (!styleAttached && styleSheet != null)
        {
            root.styleSheets.Add(styleSheet);
            styleAttached = true;
        }

        nameLabel = root.Q<Label>(LabelElementName);
        guildPrefixLabel = root.Q<Label>(GuildPrefixLabelElementName);
        nameRow = root.Q<VisualElement>(NameRowElementName);
        if (nameLabel != null)
        {
            nameLabel.pickingMode = PickingMode.Ignore;
            nameLabel.enableRichText = false;
        }
        else
        {
            nameLabel = new Label
            {
                name = LabelElementName,
                pickingMode = PickingMode.Ignore
            };
            nameLabel.enableRichText = false;
            if (nameRow == null)
            {
                nameRow = new VisualElement
                {
                    name = NameRowElementName,
                    pickingMode = PickingMode.Ignore
                };
                nameRow.AddToClassList("nameplate-name-row");
                root.Add(nameRow);
            }

            nameRow.Add(nameLabel);
        }

        if (guildPrefixLabel != null)
        {
            guildPrefixLabel.pickingMode = PickingMode.Ignore;
            guildPrefixLabel.enableRichText = false;
        }
        else if (nameRow != null)
        {
            guildPrefixLabel = new Label
            {
                name = GuildPrefixLabelElementName,
                pickingMode = PickingMode.Ignore
            };
            guildPrefixLabel.enableRichText = false;
            nameRow.Insert(0, guildPrefixLabel);
        }

        healthBarDamageTrail = root.Q<VisualElement>(HealthBarDamageTrailElementName);
        if (healthBarDamageTrail != null)
        {
            healthBarDamageTrail.pickingMode = PickingMode.Ignore;
        }

        healthBarFill = root.Q<VisualElement>(HealthBarFillElementName);
        if (healthBarFill != null)
        {
            healthBarFill.pickingMode = PickingMode.Ignore;
        }

        healthBarSlice = root.Q<VisualElement>(HealthBarSliceElementName);
        if (healthBarSlice != null)
        {
            healthBarSlice.pickingMode = PickingMode.Ignore;
            healthBarSlice.style.display = DisplayStyle.None;
            healthBarSlice.style.opacity = 0f;
        }
    }

    private void RefreshDisplayName()
    {
        if (nameLabel == null)
        {
            return;
        }

        if (guildPrefixLabel != null)
        {
            string guildPrefix = NormalizeGuildAbbreviation(guildAbbreviationOverride);
            guildPrefixLabel.text = string.IsNullOrWhiteSpace(guildPrefix) ? string.Empty : $"[{guildPrefix}]";
            guildPrefixLabel.style.display = string.IsNullOrWhiteSpace(guildPrefix)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        nameLabel.text = UiTextSanitizer.SanitizeForLabel(ResolveDisplayName(), collapseWhitespace: true);
    }

    private string ResolveDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(displayNameOverride))
        {
            return displayNameOverride.Trim();
        }

        string rawName = gameObject.name;
        if (rawName.EndsWith(CloneSuffix, StringComparison.Ordinal))
        {
            rawName = rawName.Substring(0, rawName.Length - CloneSuffix.Length).TrimEnd();
        }

        return string.IsNullOrWhiteSpace(rawName) ? "Unknown" : rawName;
    }

    private static string NormalizeGuildAbbreviation(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = UiTextSanitizer.SanitizeForLabel(value.Trim().ToUpperInvariant(), collapseWhitespace: true);
        if (normalized.Length != 3)
        {
            return string.Empty;
        }

        for (int index = 0; index < normalized.Length; index++)
        {
            if (!char.IsLetter(normalized[index]))
            {
                return string.Empty;
            }
        }

        return normalized;
    }

    private bool TryGetCamera(out Camera renderCamera)
    {
        renderCamera = cachedCamera;

        if (renderCamera == null)
        {
            renderCamera = Camera.main;
            cachedCamera = renderCamera;
        }

        return renderCamera != null;
    }

    private void ResolveHealthSystem()
    {
        healthSystem = GetComponent<IHealthSystem>();
        if (healthSystem == null)
        {
            healthSystem = GetComponentInParent<IHealthSystem>();
        }

        trackedTransform = healthSystem is Component component ? component.transform : transform;
        hasAnchorOffset = false;
        anchorUsesRendererBounds = false;
    }

    private void RebuildAnchorOffset()
    {
        if (trackedTransform == null)
        {
            trackedTransform = transform;
        }

        Renderer[] renderers = trackedTransform.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds bounds = default;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer targetRenderer = renderers[i];
            if (!IsAnchorRenderer(targetRenderer))
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = targetRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(targetRenderer.bounds);
            }
        }

        anchorUsesRendererBounds = hasBounds;

        Vector3 anchorWorld;
        if (hasBounds)
        {
            anchorWorld = bounds.center;
            anchorWorld.y = placeUnderTarget
                ? bounds.min.y - HealthBarAnchorVerticalOffset
                : bounds.max.y + HealthBarAnchorVerticalOffset;
        }
        else
        {
            float signedOffset = placeUnderTarget ? -HealthBarAnchorVerticalOffset : HealthBarAnchorVerticalOffset;
            anchorWorld = trackedTransform.position + (Vector3.up * signedOffset);
        }

        localAnchorOffset = trackedTransform.InverseTransformPoint(anchorWorld);
        hasAnchorOffset = true;
    }

    private static bool IsAnchorRenderer(Renderer renderer)
    {
        if (renderer == null || !renderer.gameObject.activeInHierarchy)
        {
            return false;
        }

        return renderer is not ParticleSystemRenderer &&
               renderer is not TrailRenderer &&
               renderer is not LineRenderer;
    }

    private static bool IsOutsideViewport(Vector3 viewport, float padding)
    {
        return viewport.x < -padding ||
               viewport.x > 1f + padding ||
               viewport.y < -padding ||
               viewport.y > 1f + padding;
    }

    private void UpdateHealthDisplay(int currentHealth, int maxHealth, float healthPercent, float deltaTime)
    {
        healthPercent = Mathf.Clamp01(healthPercent);

        if (!hasAnimatedHealthState)
        {
            targetHealthPercent = healthPercent;
            animatedHealthPercent = healthPercent;
            damageTrailPercent = healthPercent;
            displayedHealthPercent = healthPercent;
            damageTrailDelayRemaining = 0f;
            sliceAnimationElapsed = SliceAnimationDuration;
            hasAnimatedHealthState = true;
        }
        else if (displayedHealth != currentHealth ||
                 displayedMaxHealth != maxHealth ||
                 !Mathf.Approximately(displayedHealthPercent, healthPercent))
        {
            if (healthPercent < targetHealthPercent)
            {
                sliceAnimationStartPercent = Mathf.Max(animatedHealthPercent, damageTrailPercent);
                sliceAnimationEndPercent = healthPercent;
                sliceAnimationElapsed = 0f;
                damageTrailDelayRemaining = DamageTrailShrinkDelay;
            }
            else if (healthPercent > targetHealthPercent)
            {
                animatedHealthPercent = healthPercent;
                damageTrailPercent = healthPercent;
                damageTrailDelayRemaining = 0f;
                sliceAnimationElapsed = SliceAnimationDuration;
            }

            targetHealthPercent = healthPercent;
            displayedHealthPercent = healthPercent;
        }

        displayedHealth = currentHealth;
        displayedMaxHealth = maxHealth;
        AnimateHealthBar(deltaTime);
    }

    private void AnimateHealthBar(float deltaTime)
    {
        if (healthBarFill != null)
        {
            animatedHealthPercent = Mathf.MoveTowards(
                animatedHealthPercent,
                targetHealthPercent,
                deltaTime * HealthFillAnimationSpeed);
            healthBarFill.style.width = new Length(animatedHealthPercent * 100f, LengthUnit.Percent);
        }

        if (healthBarDamageTrail != null)
        {
            if (damageTrailPercent < targetHealthPercent)
            {
                damageTrailPercent = Mathf.MoveTowards(
                    damageTrailPercent,
                    targetHealthPercent,
                    deltaTime * (HealthFillAnimationSpeed + 1f));
            }
            else if (damageTrailPercent > targetHealthPercent)
            {
                if (damageTrailDelayRemaining > 0f)
                {
                    damageTrailDelayRemaining = Mathf.Max(0f, damageTrailDelayRemaining - deltaTime);
                }
                else
                {
                    damageTrailPercent = Mathf.MoveTowards(
                        damageTrailPercent,
                        targetHealthPercent,
                        deltaTime * DamageTrailShrinkSpeed);
                }
            }

            healthBarDamageTrail.style.width = new Length(damageTrailPercent * 100f, LengthUnit.Percent);
        }

        if (healthBarSlice == null)
        {
            return;
        }

        if (sliceAnimationElapsed < SliceAnimationDuration)
        {
            sliceAnimationElapsed = Mathf.Min(SliceAnimationDuration, sliceAnimationElapsed + deltaTime);
            float progress = Mathf.Clamp01(sliceAnimationElapsed / SliceAnimationDuration);
            float easedProgress = 1f - Mathf.Pow(1f - progress, 3f);
            float slicePercent = Mathf.Lerp(sliceAnimationStartPercent, sliceAnimationEndPercent, easedProgress);

            healthBarSlice.style.display = DisplayStyle.Flex;
            healthBarSlice.style.left = new Length(slicePercent * 100f, LengthUnit.Percent);
            healthBarSlice.style.opacity = 1f - progress;
        }
        else
        {
            healthBarSlice.style.display = DisplayStyle.None;
            healthBarSlice.style.opacity = 0f;
        }
    }

    private void SetVisible(bool visible)
    {
        if (worldSpaceDocument == null || worldSpaceDocument.rootVisualElement == null)
        {
            isVisible = visible;
            return;
        }

        if (isVisible == visible)
        {
            return;
        }

        worldSpaceDocument.rootVisualElement.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        isVisible = visible;
    }
}
