using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerShipVisualController : MonoBehaviour
{
    private readonly Dictionary<string, GameObject> shipVisuals = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> shipOrder = new List<string>();

    private Player player;
    private string currentShipId = string.Empty;

    public string CurrentShipId => currentShipId;

    public IReadOnlyList<string> AvailableShipIds => shipOrder;

    private void Awake()
    {
        player = GetComponent<Player>();
        if (player == null)
        {
            player = GetComponentInParent<Player>();
        }

        RefreshShipVisuals();
    }

    private void OnEnable()
    {
        if (player != null)
        {
            player.OnSelectedShipChanged += OnSelectedShipChanged;
        }

        RefreshShipVisuals();
        ApplyPlayerSelection();
    }

    private void OnDisable()
    {
        if (player != null)
        {
            player.OnSelectedShipChanged -= OnSelectedShipChanged;
        }
    }

    private void OnTransformChildrenChanged()
    {
        RefreshShipVisuals();
        ApplyCurrentSelection();
    }

    public bool HasShipVisual(string shipId)
    {
        return shipVisuals.ContainsKey(NormalizeShipId(shipId));
    }

    public bool TrySetShipVisual(string shipId)
    {
        string normalizedShipId = NormalizeShipId(shipId);
        if (string.IsNullOrEmpty(normalizedShipId) || !shipVisuals.ContainsKey(normalizedShipId))
        {
            return false;
        }

        if (player != null && player.IsOwner)
        {
            if (!player.RequestShipSelection(normalizedShipId))
            {
                return false;
            }
        }

        if (!string.Equals(currentShipId, normalizedShipId, StringComparison.OrdinalIgnoreCase))
        {
            currentShipId = normalizedShipId;
            ApplyCurrentSelection();
        }

        return true;
    }

    public void RefreshShipVisuals()
    {
        shipVisuals.Clear();
        shipOrder.Clear();
        RegisterShipVisualRecursive(transform);

        string normalizedCurrent = NormalizeShipId(currentShipId);
        if (!string.IsNullOrEmpty(normalizedCurrent) && shipVisuals.ContainsKey(normalizedCurrent))
        {
            currentShipId = normalizedCurrent;
            return;
        }

        currentShipId = shipOrder.Count > 0 ? shipOrder[0] : string.Empty;
    }

    private void ApplyCurrentSelection()
    {
        if (shipVisuals.Count == 0)
        {
            return;
        }

        string normalizedCurrent = NormalizeShipId(currentShipId);
        GameObject activeShip = null;

        if (!string.IsNullOrEmpty(normalizedCurrent))
        {
            shipVisuals.TryGetValue(normalizedCurrent, out activeShip);
        }

        if (activeShip == null && shipOrder.Count > 0)
        {
            string fallbackShipId = shipOrder[0];
            shipVisuals.TryGetValue(fallbackShipId, out activeShip);
            currentShipId = fallbackShipId;
        }

        foreach (KeyValuePair<string, GameObject> shipEntry in shipVisuals)
        {
            if (shipEntry.Value == null)
            {
                continue;
            }

            shipEntry.Value.SetActive(ReferenceEquals(shipEntry.Value, activeShip));
        }
    }

    private void ApplyPlayerSelection()
    {
        if (player != null && !string.IsNullOrWhiteSpace(player.SelectedShipId))
        {
            currentShipId = NormalizeShipId(player.SelectedShipId);
        }

        ApplyCurrentSelection();
    }

    private void OnSelectedShipChanged(string shipId)
    {
        currentShipId = NormalizeShipId(shipId);
        ApplyCurrentSelection();
    }

    private void RegisterShipVisualRecursive(Transform root)
    {
        if (root == null)
        {
            return;
        }

        for (int index = 0; index < root.childCount; index++)
        {
            Transform child = root.GetChild(index);
            if (child == null)
            {
                continue;
            }

            GameObject childObject = child.gameObject;
            string shipId = childObject != null ? NormalizeShipId(childObject.name) : string.Empty;
            if (!string.IsNullOrEmpty(shipId) &&
                MarketShipCatalogRuntime.TryGetShip(shipId, out _) &&
                !shipVisuals.ContainsKey(shipId))
            {
                shipVisuals.Add(shipId, childObject);
                shipOrder.Add(shipId);
            }

            RegisterShipVisualRecursive(child);
        }
    }

    private static string NormalizeShipId(string shipId)
    {
        return MarketShipCatalogRuntime.NormalizeShipId(shipId);
    }
}
