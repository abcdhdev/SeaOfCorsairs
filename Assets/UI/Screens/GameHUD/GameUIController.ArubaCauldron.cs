public partial class GameUIController
{
    public void ShowArubaCauldronFromWorld()
    {
        if (root == null)
        {
            return;
        }

        EnsureArubaCauldronSection();
        SetTopMenuShieldDropdownVisible(false);
        CloseGuildManagement();
        CloseMarket();
        CloseSettingsMenu();
        shipSectionController?.Hide();
        CloseAmmoMenu();
        CloseHarpoonMenu();
        CloseActionItemMenu();
        StopSkillDrag();
        ClearPendingSourcePress();
        CloseIslandBuilding();
        arubaCauldronController?.Show();
    }

    private void CloseArubaCauldron()
    {
        arubaCauldronController?.Hide();
    }
}
