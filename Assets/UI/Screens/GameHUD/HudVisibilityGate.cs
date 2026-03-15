using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
public sealed class HudVisibilityGate : MonoBehaviour
{
    private const string HudRootName = "HudRoot";
    private const float PollIntervalSeconds = 0.25f;

    private bool _subscribed;
    private bool _lastVisible;
    private int _lastHudRootsSeen;
    private float _nextPollTime;

    private void Awake()
    {
        if (Application.isBatchMode)
        {
            enabled = false;
            return;
        }

        DontDestroyOnLoad(gameObject);
        SetHudVisible(false);
    }

    private void OnEnable()
    {
        Subscribe();
        Evaluate();
    }

    private void OnDisable()
    {
        Unsubscribe();
        SetHudVisible(false);
    }

    private void Update()
    {
        // Polling is mainly to handle scene reloads / UIDocument rebuilds without wiring a bunch of UI events.
        if (Time.unscaledTime < _nextPollTime)
        {
            return;
        }

        _nextPollTime = Time.unscaledTime + PollIntervalSeconds;
        Evaluate();
    }

    private void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        PlayerManager.OnLocalPlayerSpawned += OnLocalPlayerSpawned;
        PlayerManager.OnPlayerRemoved += OnAnyPlayerRemoved;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }

        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        PlayerManager.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;
        PlayerManager.OnPlayerRemoved -= OnAnyPlayerRemoved;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        _subscribed = false;
    }

    private void OnClientConnected(ulong clientId) => Evaluate();

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            SetHudVisible(false);
        }

        Evaluate();
    }

    private void OnLocalPlayerSpawned(Player player) => Evaluate();

    private void OnAnyPlayerRemoved(Player player) => Evaluate();

    private void Evaluate()
    {
        bool shouldShowHud = IsInWorldReady();
        int hudRootsSeen = CountHudRoots();

        // Also re-apply when UI documents are rebuilt (scene load / domain reload / UIDocument refresh).
        if (shouldShowHud != _lastVisible || hudRootsSeen != _lastHudRootsSeen)
        {
            _lastVisible = shouldShowHud;
            _lastHudRootsSeen = hudRootsSeen;
            SetHudVisible(shouldShowHud);
        }
    }

    private static bool IsInWorldReady()
    {
        if (NetworkManager.Singleton == null)
        {
            return false;
        }

        if (!NetworkManager.Singleton.IsClient || !NetworkManager.Singleton.IsConnectedClient)
        {
            return false;
        }

        // Local player must exist and be spawned. This is the "in-world" threshold.
        if (Player.LocalPlayer != null && Player.LocalPlayer.IsSpawned)
        {
            return true;
        }

        if (PlayerManager.Instance != null && PlayerManager.Instance.LocalPlayer != null && PlayerManager.Instance.LocalPlayer.IsSpawned)
        {
            return true;
        }

        return false;
    }

    private static void SetHudVisible(bool visible)
    {
        UIDocument[] docs = FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
        for (int i = 0; i < docs.Length; i++)
        {
            var doc = docs[i];
            if (doc == null || doc.rootVisualElement == null)
            {
                continue;
            }

            VisualElement hudRoot = doc.rootVisualElement.Q<VisualElement>(HudRootName);
            if (hudRoot == null)
            {
                continue;
            }

            hudRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private static int CountHudRoots()
    {
        int count = 0;
        UIDocument[] docs = FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
        for (int i = 0; i < docs.Length; i++)
        {
            var doc = docs[i];
            if (doc == null || doc.rootVisualElement == null)
            {
                continue;
            }

            if (doc.rootVisualElement.Q<VisualElement>(HudRootName) != null)
            {
                count++;
            }
        }

        return count;
    }
}
