using PrimeTween;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// World-space floating damage or heal number.
/// Spawned by <see cref="DamageNumberService"/> and destroyed after the animation completes.
/// </summary>
public class DamageNumber : MonoBehaviour
{
    private const float Duration = 1.3f;
    private const float FloatWorldDistance = 8f;
    private const float ScalePunchPeak = 1.4f;
    private const float ScalePunchDuration = 0f;
    private const string PanelSettingsResourcePath = "Worldspace/WorldNameplatePanelSettings";
    private const string DamageNumberStyleSheetResourcePath = "Worldspace/DamageNumbers/DamageNumberStyles";

    private static readonly Color DamageColor = new Color(1f, 0.32f, 0.22f, 1f);
    private static readonly Color HealColor = new Color(0.28f, 0.91f, 0.35f, 1f);
    private static readonly Color BlackGunpowderColor = new Color(1f, 0.86f, 0.24f, 1f);
    private static readonly Color AgwesArmorPlatingColor = new Color(0.73f, 0.45f, 0.95f, 1f);
    private static StyleSheet damageNumberStyleSheet;

    private Camera cam;
    private UIDocument document;
    private VisualElement content;
    private VisualElement icon;
    private Label label;
    private float startY;

    public void Initialize(
        int amount,
        bool isHeal,
        Camera camera,
        DamageNumberEffectStyle effectStyle = DamageNumberEffectStyle.Default)
    {
        cam = camera;
        startY = transform.position.y;

        PanelSettings panelSettings = Resources.Load<PanelSettings>(PanelSettingsResourcePath);
        if (panelSettings == null)
        {
            Debug.LogWarning("DamageNumber: Missing PanelSettings at Resources/Worldspace/WorldNameplatePanelSettings.");
            Destroy(gameObject);
            return;
        }

        document = gameObject.AddComponent<UIDocument>();
        document.panelSettings = panelSettings;
        document.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Dynamic;
        document.sortingOrder = 20;

        VisualElement root = document.rootVisualElement;
        if (root == null)
        {
            Destroy(gameObject);
            return;
        }

        root.pickingMode = PickingMode.Ignore;
        root.style.justifyContent = Justify.Center;
        root.style.alignItems = Align.Center;
        root.style.flexDirection = FlexDirection.Row;

        damageNumberStyleSheet ??= Resources.Load<StyleSheet>(DamageNumberStyleSheetResourcePath);
        if (damageNumberStyleSheet != null)
        {
            root.styleSheets.Add(damageNumberStyleSheet);
        }

        content = new VisualElement();
        content.pickingMode = PickingMode.Ignore;
        content.AddToClassList("damage-number-content");

        icon = new VisualElement();
        icon.pickingMode = PickingMode.Ignore;
        icon.AddToClassList("damage-number-icon");
        icon.style.display = DisplayStyle.None;
        content.Add(icon);

        label = new Label(amount.ToString());
        label.pickingMode = PickingMode.Ignore;
        label.style.fontSize = 64;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.color = isHeal ? HealColor : DamageColor;
        label.style.unityTextOutlineColor = new Color(0f, 0f, 0f, 0.9f);
        label.style.unityTextOutlineWidth = 10f;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        content.Add(label);
        root.Add(content);

        ApplyEffectStyle(isHeal, effectStyle);

        Tween.Custom(this, 0f, 1f, Duration, (self, t) =>
        {
            self.ApplyAnimation(t);
        }, ease: Ease.OutQuad).OnComplete(() => Destroy(gameObject));

        if (!isHeal && ScalePunchDuration > 0f && ScalePunchPeak > 1f)
        {
            transform.localScale = Vector3.one * ScalePunchPeak;
            Tween.Custom(this, ScalePunchPeak, 1f, ScalePunchDuration, (self, s) =>
            {
                self.transform.localScale = Vector3.one * s;
            }, ease: Ease.OutQuad);
        }
    }

    private void ApplyEffectStyle(bool isHeal, DamageNumberEffectStyle effectStyle)
    {
        if (label == null)
        {
            return;
        }

        if (isHeal)
        {
            label.style.color = HealColor;
            if (icon != null)
            {
                icon.style.display = DisplayStyle.None;
            }

            return;
        }

        label.style.color = DamageColor;
        if (icon != null)
        {
            icon.style.backgroundImage = new StyleBackground();
            icon.style.display = DisplayStyle.None;
        }

        Texture2D iconTexture = ActionItemIconCatalog.GetDamageIcon(effectStyle);
        switch (effectStyle)
        {
            case DamageNumberEffectStyle.BlackGunpowder:
                label.style.color = BlackGunpowderColor;
                break;

            case DamageNumberEffectStyle.AgwesArmorPlating:
                label.style.color = AgwesArmorPlatingColor;
                break;
        }

        if (icon != null && iconTexture != null)
        {
            icon.style.backgroundImage = new StyleBackground(iconTexture);
            icon.style.display = DisplayStyle.Flex;
        }
    }

    private void ApplyAnimation(float t)
    {
        Vector3 pos = transform.position;
        pos.y = startY + FloatWorldDistance * t;
        transform.position = pos;

        if (label?.panel != null)
        {
            label.style.opacity = 1f - t;
        }
    }

    private void LateUpdate()
    {
        if (cam != null)
        {
            transform.rotation = cam.transform.rotation;
        }
    }
}
