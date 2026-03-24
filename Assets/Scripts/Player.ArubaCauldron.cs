using System;
using Unity.Netcode;

public partial class Player
{
    public event Action<ArubaCauldronRitualResultData> OnArubaCauldronRitualResult = delegate { };

    public bool RequestArubaCauldronRitual(int quantity)
    {
        if (!IsOwner || !ArubaCauldronRuntime.IsValidRitualQuantity(quantity))
        {
            return false;
        }

        RequestArubaCauldronRitualServerRpc(quantity);
        return true;
    }

    public void NotifyArubaCauldronRitualResult(
        int quantity,
        int mojoSpent,
        int diamondSpent,
        string rewardSnapshot,
        bool success,
        string message)
    {
        if (!IsServer)
        {
            return;
        }

        PushArubaCauldronRitualResultClientRpc(
            quantity,
            mojoSpent,
            diamondSpent,
            rewardSnapshot ?? string.Empty,
            success,
            message ?? string.Empty);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestArubaCauldronRitualServerRpc(int quantity)
    {
        if (MultiplayerController.Instance == null)
        {
            NotifyArubaCauldronRitualResult(quantity, 0, 0, string.Empty, false, "The multiplayer controller is not ready.");
            return;
        }

        MultiplayerController.Instance.RequestArubaCauldronRitual(this, quantity);
    }

    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
    private void PushArubaCauldronRitualResultClientRpc(
        int quantity,
        int mojoSpent,
        int diamondSpent,
        string rewardSnapshot,
        bool success,
        string message)
    {
        OnArubaCauldronRitualResult?.Invoke(
            new ArubaCauldronRitualResultData(
                success,
                message,
                quantity,
                mojoSpent,
                diamondSpent,
                rewardSnapshot));
    }
}
