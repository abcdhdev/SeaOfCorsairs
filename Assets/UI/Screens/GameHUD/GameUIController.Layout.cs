using UnityEngine;
using UnityEngine.UIElements;

public partial class GameUIController
{
    private const string TopBarMountName = "TopBarMount";
    private const string MinimapMountName = "MinimapMount";
    private const string CoordinateRulerMountName = "CoordinateRulerMount";
    private const string ChatMountName = "ChatMount";
    private const string ActionBarMountName = "ActionBarMount";
    private const string IslandEditMountName = "IslandEditMount";
    private const string DeadOverlayMountName = "DeadOverlayMount";

    private const string TopBarFragmentPath = "GameHUD/Fragments/TopBar";
    private const string MinimapFragmentPath = "GameHUD/Fragments/Minimap";
    private const string CoordinateRulerFragmentPath = "GameHUD/Fragments/CoordinateRuler";
    private const string ChatFragmentPath = "GameHUD/Fragments/ChatPanel";
    private const string ActionBarFragmentPath = "GameHUD/Fragments/ActionBar";
    private const string IslandEditFragmentPath = "GameHUD/Fragments/IslandEditBar";
    private const string DeadOverlayFragmentPath = "GameHUD/Fragments/DeadOverlay";

    private void EnsureHudLayoutComposed()
    {
        if (root == null)
        {
            return;
        }

        EnsureFragmentMounted(TopBarMountName, "TopMenuBar", TopBarFragmentPath);
        EnsureFragmentMounted(MinimapMountName, "MinimapRoot", MinimapFragmentPath);
        EnsureFragmentMounted(CoordinateRulerMountName, "CoordinateRulerRoot", CoordinateRulerFragmentPath);
        EnsureFragmentMounted(ChatMountName, "MmoChatRoot", ChatFragmentPath);
        EnsureFragmentMounted(ActionBarMountName, "BottomHudContainer", ActionBarFragmentPath);
        EnsureFragmentMounted(IslandEditMountName, "IslandEditRoot", IslandEditFragmentPath);
        EnsureFragmentMounted(DeadOverlayMountName, "DeadOverlayRoot", DeadOverlayFragmentPath);
    }

    private void EnsureFragmentMounted(string mountName, string expectedRootName, string resourcePath)
    {
        VisualElement mount = root.Q<VisualElement>(mountName);
        if (mount == null)
        {
            return;
        }

        StretchMountToHud(mount);

        if (mount.Q<VisualElement>(expectedRootName) != null)
        {
            return;
        }

        VisualTreeAsset fragment = Resources.Load<VisualTreeAsset>(resourcePath);
        if (fragment == null)
        {
            Debug.LogWarning($"GameUIController: Missing HUD fragment '{resourcePath}'.");
            return;
        }

        TemplateContainer fragmentInstance = fragment.Instantiate();
        StretchFragmentInstance(fragmentInstance);

        mount.Clear();
        mount.Add(fragmentInstance);
    }

    private static void StretchMountToHud(VisualElement mount)
    {
        if (mount == null)
        {
            return;
        }

        mount.pickingMode = PickingMode.Ignore;
        mount.style.position = Position.Absolute;
        mount.style.top = 0;
        mount.style.right = 0;
        mount.style.bottom = 0;
        mount.style.left = 0;
        mount.style.overflow = Overflow.Visible;
    }

    private static void StretchFragmentInstance(TemplateContainer fragmentInstance)
    {
        if (fragmentInstance == null)
        {
            return;
        }

        fragmentInstance.pickingMode = PickingMode.Ignore;
        fragmentInstance.style.position = Position.Absolute;
        fragmentInstance.style.top = 0;
        fragmentInstance.style.right = 0;
        fragmentInstance.style.bottom = 0;
        fragmentInstance.style.left = 0;
        fragmentInstance.style.overflow = Overflow.Visible;
    }
}
