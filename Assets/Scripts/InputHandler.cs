using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class InputHandler : MonoBehaviour
{
    private const string WaterLayerName = "Water";

    private GameObject _player;

    private Camera _mainCamera;
    private ClickToMove _clickToMove;
    private int _waterLayer = -1;
    private bool _loggedFallbackCameraSelection;

    private readonly List<RaycastResult> _uiRaycastResults = new List<RaycastResult>(8);
    private PointerEventData _uiPointerEventData;
    private EventSystem _cachedEventSystem;

    [Header("Input")] [SerializeField] private InputActionReference clickAction;
    [SerializeField] private InputActionReference fireAction;
    [SerializeField] private InputActionReference doubleClickAction;
    [SerializeField, Min(0.1f)] private float maxClickRayDistance = 1000f;
    [SerializeField, Min(0.1f)] private float navMeshClickSampleDistance = 2f;
    [Header("Target Selection")]
    [SerializeField, Min(0f)] private float combatTargetSelectionRadius = 2.5f;

    private void Awake()
    {
        _mainCamera = Camera.main;
        _clickToMove = FindFirstObjectByType<ClickToMove>();
        _waterLayer = LayerMask.NameToLayer(WaterLayerName);
    }

    private void Start()
    {
        Debug.Log("InputHandler Started");
    }

    private void OnEnable()
    {
        if (clickAction != null && clickAction.action != null)
        {
            clickAction.action.Enable();
            clickAction.action.performed += OnClick;
        }

        if (fireAction != null && fireAction.action != null)
        {
            fireAction.action.Enable();
            fireAction.action.performed += OnFire;
        }
        else
        {
            Debug.LogError("InputHandler: Fire Action is not assigned! Please assign 'Fire' action in Inspector.");
        }

        if (doubleClickAction != null && doubleClickAction.action != null)
        {
            doubleClickAction.action.Enable();
            doubleClickAction.action.performed += OnDoubleClick;
        }
        else
        {
            Debug.LogError(
                "InputHandler: DoubleClick Action is not assigned! Please assign 'DoubleClick' action in Inspector.");
        }
    }

    private void OnDisable()
    {
        _uiRaycastResults.Clear();
        _uiPointerEventData = null;
        _cachedEventSystem = null;

        if (clickAction != null && clickAction.action != null)
        {
            clickAction.action.performed -= OnClick;
            clickAction.action.Disable();
        }

        if (fireAction != null && fireAction.action != null)
        {
            fireAction.action.performed -= OnFire;
            fireAction.action.Disable();
        }

        if (doubleClickAction != null && doubleClickAction.action != null)
        {
            doubleClickAction.action.performed -= OnDoubleClick;
            doubleClickAction.action.Disable();
        }
    }

    private bool EnsurePlayerFound()
    {
        if (_player != null && _clickToMove != null) return true;

        if (Player.LocalPlayer != null)
        {
            _player = Player.LocalPlayer.gameObject;
            _clickToMove = _player.GetComponent<ClickToMove>();
            return _clickToMove != null;
        }

        return false;
    }

    private bool TryGetLocalPlayerComponent(out Player localPlayer)
    {
        localPlayer = null;
        if (!EnsurePlayerFound() || _player == null)
        {
            return false;
        }

        localPlayer = _player.GetComponent<Player>();
        return localPlayer != null;
    }

    private bool IsLocalPlayerAlive()
    {
        if (!TryGetLocalPlayerComponent(out Player localPlayer))
        {
            return false;
        }

        return !localPlayer.IsDead;
    }

    public void OnClick(InputAction.CallbackContext context)
    {
        if (!TryResolveMainCamera())
        {
            Debug.LogError("InputHandler could not resolve a gameplay camera. Ensure an active camera is tagged MainCamera.");
            return;
        }

        if (!EnsurePlayerFound()) return;
        if (!IsLocalPlayerAlive()) return;
        if (!TryGetPointerPosition(out Vector2 pointerPosition)) return;

        // Blur chat input when the player clicks outside the chat panel.
        ChatController.TryBlurChatInput();

        if (UIToolkitRaycastChecker.TryGetBlockingElementAtPointer(pointerPosition, out var blockingElement))
        {
            string blockingElementName = string.IsNullOrWhiteSpace(blockingElement.name)
                ? blockingElement.GetType().Name
                : blockingElement.name;
            float topOriginY = Screen.height - pointerPosition.y;
            Debug.Log($"[InputHandler] Click blocked: pointer over UI '{blockingElementName}' at {pointerPosition} (top-origin y: {topOriginY:0.##}, screen h: {Screen.height}, y-origin mode: {UIToolkitRaycastChecker.PointerYAxisOriginDebugName})");
            return;
        }

        Ray ray = _mainCamera.ScreenPointToRay(pointerPosition);
        if (TryHandleClickRay(ray))
        {
            return;
        }
    }

    public void OnFire(InputAction.CallbackContext context)
    {
        if (!TryGetLocalPlayerComponent(out Player localPlayer)) return;
        if (localPlayer.IsDead) return;

        GameObject selectedTarget = SelectObject.Instance != null ? SelectObject.Instance.SelectedTarget : null;
        if (selectedTarget != null)
        {
            Debug.Log($"[InputHandler] F Pressed - Starting continuous attack on selected target: {selectedTarget.name}");
            localPlayer.StartAttack(selectedTarget);
        }
    }

    public void OnDoubleClick(InputAction.CallbackContext context)
    {
        if (!TryResolveMainCamera())
        {
            Debug.LogError("InputHandler could not resolve a gameplay camera. Ensure an active camera is tagged MainCamera.");
            return;
        }

        if (!TryGetLocalPlayerComponent(out Player localPlayer)) return;
        if (localPlayer.IsDead) return;
        if (!TryGetPointerPosition(out Vector2 pointerPosition)) return;

        // Blur chat input when the player double-clicks outside the chat panel.
        ChatController.TryBlurChatInput();

        if (UIToolkitRaycastChecker.TryGetBlockingElementAtPointer(pointerPosition, out var blockingElement))
        {
            string blockingElementName = string.IsNullOrWhiteSpace(blockingElement.name)
                ? blockingElement.GetType().Name
                : blockingElement.name;
            float topOriginY = Screen.height - pointerPosition.y;
            Debug.Log($"[InputHandler] Double click blocked: pointer over UI '{blockingElementName}' at {pointerPosition} (top-origin y: {topOriginY:0.##}, screen h: {Screen.height}, y-origin mode: {UIToolkitRaycastChecker.PointerYAxisOriginDebugName})");
            return;
        }

        Ray ray = _mainCamera.ScreenPointToRay(pointerPosition);
        if (TryFindCombatTargetFromRay(ray, out GameObject combatTarget))
        {
            Debug.Log($"[InputHandler] Double Click on target: {combatTarget.name}. Selecting and starting continuous attack.");
            if (SelectObject.Instance != null)
            {
                SelectObject.Instance.Select(combatTarget);
            }

            localPlayer.StartAttack(combatTarget);
        }
    }

    private bool TryHandleClickRay(Ray ray)
    {
        EvaluateClickRay(
            ray,
            out bool selfHit,
            out bool hasMoveHit,
            out Vector3 moveHitPoint,
            out bool hasWaterHit,
            out Vector3 waterHitPoint,
            out float maxTargetDistance,
            out bool blockedByGeometry);
        if (selfHit)
        {
            // Clicking on yourself should not issue a move command through your own hull.
            return true;
        }

        if (TryFindCombatTargetFromRay(ray, maxTargetDistance, out GameObject combatTarget))
        {
            if (SelectObject.Instance != null)
            {
                SelectObject.Instance.Select(combatTarget);
            }

            return true;
        }

        if (TryResolveMovementDestination(ray, hasMoveHit, moveHitPoint, hasWaterHit, waterHitPoint, out Vector3 targetPoint))
        {
            Debug.Log($"Moving player to: {targetPoint}");
            _clickToMove.OnClick(targetPoint);
            return true;
        }

        return blockedByGeometry;
    }

    private bool TryFindCombatTargetFromRay(Ray ray, out GameObject target)
    {
        if (!TryGetMaxCombatTargetDistance(ray, out float maxTargetDistance))
        {
            target = null;
            return false;
        }

        return TryFindCombatTargetFromRay(ray, maxTargetDistance, out target);
    }

    private bool TryFindCombatTargetFromRay(Ray ray, float maxTargetDistance, out GameObject target)
    {
        target = null;
        if (combatTargetSelectionRadius <= 0f || maxTargetDistance <= 0f)
        {
            return false;
        }

        return CombatTargetingUtility.TryFindTargetAlongRay(
            ray,
            _player,
            Mathf.Min(maxTargetDistance, maxClickRayDistance),
            combatTargetSelectionRadius,
            out target);
    }

    private bool TryGetMaxCombatTargetDistance(Ray ray, out float maxTargetDistance)
    {
        EvaluateClickRay(ray, out bool selfHit, out _, out _, out _, out _, out maxTargetDistance, out _);
        return !selfHit;
    }

    private void EvaluateClickRay(
        Ray ray,
        out bool selfHit,
        out bool hasMoveHit,
        out Vector3 moveHitPoint,
        out bool hasWaterHit,
        out Vector3 waterHitPoint,
        out float maxTargetDistance,
        out bool blockedByGeometry)
    {
        selfHit = false;
        hasMoveHit = false;
        moveHitPoint = default;
        hasWaterHit = false;
        waterHitPoint = default;
        maxTargetDistance = maxClickRayDistance;
        blockedByGeometry = false;

        RaycastHit[] hits = Physics.RaycastAll(ray, maxClickRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null)
            {
                continue;
            }

            if (IsLocalPlayerHit(hit.collider))
            {
                selfHit = true;
                return;
            }

            if (CombatTargetingUtility.IsTargetableCollider(hit.collider))
            {
                continue;
            }

            if (IsWaterLayerHit(hit.collider))
            {
                if (!hasMoveHit)
                {
                    hasMoveHit = true;
                    moveHitPoint = hit.point;
                }

                if (!hasWaterHit)
                {
                    hasWaterHit = true;
                    waterHitPoint = hit.point;
                }

                continue;
            }

            hasMoveHit = true;
            moveHitPoint = hit.point;
            maxTargetDistance = hit.distance;
            blockedByGeometry = true;
            return;
        }
    }

    private bool TryResolveMovementDestination(
        Ray ray,
        bool hasMoveHit,
        Vector3 moveHitPoint,
        bool hasWaterHit,
        Vector3 waterHitPoint,
        out Vector3 targetPoint)
    {
        targetPoint = default;

        if (hasWaterHit && TryProjectToNavigationPlane(waterHitPoint, out targetPoint))
        {
            return true;
        }

        if (hasMoveHit && TryProjectToNavigationPlane(moveHitPoint, out targetPoint))
        {
            return true;
        }

        return TryProjectRayToNavigationPlane(ray, out targetPoint);
    }

    private bool IsLocalPlayerHit(Collider hitCollider)
    {
        if (hitCollider == null || _player == null)
        {
            return false;
        }

        Transform playerRoot = _player.transform;
        Transform hitTransform = hitCollider.transform;
        if (hitTransform == playerRoot || hitTransform.IsChildOf(playerRoot))
        {
            return true;
        }

        Player hitPlayer = hitCollider.GetComponentInParent<Player>();
        return hitPlayer != null && hitPlayer == Player.LocalPlayer;
    }

    private bool TryResolveMainCamera()
    {
        if (IsUsableGameplayCamera(_mainCamera))
        {
            return true;
        }

        Camera taggedCamera = Camera.main;
        if (IsUsableGameplayCamera(taggedCamera))
        {
            _mainCamera = taggedCamera;
            return true;
        }

        Camera fallbackCamera = null;
        foreach (Camera candidate in FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (!IsUsableGameplayCamera(candidate))
            {
                continue;
            }

            fallbackCamera ??= candidate;

            if (candidate.targetTexture != null)
            {
                continue;
            }

            _mainCamera = candidate;
            LogFallbackCameraSelection(candidate);
            return true;
        }

        if (fallbackCamera == null)
        {
            return false;
        }

        _mainCamera = fallbackCamera;
        LogFallbackCameraSelection(fallbackCamera);
        return true;
    }

    private void LogFallbackCameraSelection(Camera camera)
    {
        if (_loggedFallbackCameraSelection || camera == null)
        {
            return;
        }

        _loggedFallbackCameraSelection = true;
        Debug.LogWarning(
            $"[InputHandler] Falling back to camera '{camera.name}' because no active camera is tagged MainCamera.");
    }

    private static bool IsUsableGameplayCamera(Camera camera)
    {
        return camera != null &&
               camera.enabled &&
               camera.gameObject.activeInHierarchy &&
               camera.cameraType == CameraType.Game;
    }

    private bool TryProjectToNavigationPlane(Vector3 worldPoint, out Vector3 targetPoint)
    {
        targetPoint = worldPoint;

        if (AstarNavigationUtility.TryGetGridGraphBounds(out Bounds graphBounds))
        {
            targetPoint.y = graphBounds.center.y;
            return true;
        }

        if (_player != null)
        {
            targetPoint.y = _player.transform.position.y;
            return true;
        }

        return false;
    }

    private bool TryProjectRayToNavigationPlane(Ray ray, out Vector3 targetPoint)
    {
        targetPoint = default;
        if (!AstarNavigationUtility.TryGetGridGraphBounds(out Bounds graphBounds))
        {
            return false;
        }

        Plane navigationPlane = new Plane(Vector3.up, new Vector3(0f, graphBounds.center.y, 0f));
        if (!navigationPlane.Raycast(ray, out float enter))
        {
            return false;
        }

        if (enter < 0f || enter > maxClickRayDistance)
        {
            return false;
        }

        targetPoint = ray.GetPoint(enter);
        targetPoint.y = graphBounds.center.y;
        return true;
    }

    private bool IsWaterLayerHit(Collider hitCollider)
    {
        if (hitCollider == null)
        {
            return false;
        }

        if (_waterLayer < 0)
        {
            // Fail open if the Water layer doesn't exist in this project configuration.
            return true;
        }

        return hitCollider.gameObject.layer == _waterLayer;
    }

    private static bool TryGetPointerPosition(out Vector2 pointerPosition)
    {
        if (Pointer.current == null)
        {
            pointerPosition = Vector2.zero;
            return false;
        }

        pointerPosition = Pointer.current.position.ReadValue();
        return true;
    }
}
