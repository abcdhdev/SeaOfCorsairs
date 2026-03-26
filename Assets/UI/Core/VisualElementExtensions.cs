using System;
using UnityEngine;
using UnityEngine.UIElements;

public static class VisualElementExtensions
{
    public static void BlockRaycasts(this VisualElement element) =>
        UIToolkitRaycastChecker.RegisterBlockingElement(element);

    public static void AllowRaycasts(this VisualElement element) =>
        UIToolkitRaycastChecker.UnregisterBlockingElement(element);

    public static bool IsBlockingRaycasts(this VisualElement element) =>
        UIToolkitRaycastChecker.IsBlockingRaycasts(element);
}

public sealed class DraggableWindowController : IDisposable
{
    private const bool LayoutDebugLogging = false;

    private readonly VisualElement boundsRoot;
    private readonly VisualElement panel;
    private readonly VisualElement handle;
    private readonly VisualElement ignoredElement;

    private bool isDragging;
    private bool shouldCenterOnNextLayout;
    private bool skipNextBoundsClamp;
    private int dragPointerId = -1;
    private Vector2 dragStartPointerPosition;
    private Vector2 dragStartPanelPosition;

    public DraggableWindowController(VisualElement boundsRoot, VisualElement panel, VisualElement handle, VisualElement ignoredElement = null)
    {
        this.boundsRoot = boundsRoot;
        this.panel = panel;
        this.handle = handle;
        this.ignoredElement = ignoredElement;

        if (this.boundsRoot != null)
        {
            this.boundsRoot.RegisterCallback<GeometryChangedEvent>(OnBoundsGeometryChanged);
        }

        if (this.panel != null)
        {
            this.panel.RegisterCallback<GeometryChangedEvent>(OnPanelGeometryChanged);
        }

        if (this.handle != null)
        {
            this.handle.RegisterCallback<PointerDownEvent>(OnHandlePointerDown);
            this.handle.RegisterCallback<PointerMoveEvent>(OnHandlePointerMove);
            this.handle.RegisterCallback<PointerUpEvent>(OnHandlePointerUp);
            this.handle.RegisterCallback<PointerCancelEvent>(OnHandlePointerCancel);
        }

        LogLayout("Constructed");
    }

    public bool IsDragging => isDragging;

    public void Dispose()
    {
        LogLayout("Dispose");
        StopDragging();

        if (boundsRoot != null)
        {
            boundsRoot.UnregisterCallback<GeometryChangedEvent>(OnBoundsGeometryChanged);
        }

        if (panel != null)
        {
            panel.UnregisterCallback<GeometryChangedEvent>(OnPanelGeometryChanged);
        }

        if (handle != null)
        {
            handle.UnregisterCallback<PointerDownEvent>(OnHandlePointerDown);
            handle.UnregisterCallback<PointerMoveEvent>(OnHandlePointerMove);
            handle.UnregisterCallback<PointerUpEvent>(OnHandlePointerUp);
            handle.UnregisterCallback<PointerCancelEvent>(OnHandlePointerCancel);
        }
    }

    public void StopDragging()
    {
        LogLayout($"StopDragging start pointerId={dragPointerId}");

        if (handle != null && dragPointerId >= 0 && handle.HasPointerCapture(dragPointerId))
        {
            handle.ReleasePointer(dragPointerId);
        }

        isDragging = false;
        dragPointerId = -1;
        dragStartPointerPosition = default;
        dragStartPanelPosition = default;
        LogLayout("StopDragging complete");
    }

    public void CenterInBounds()
    {
        if (panel == null)
        {
            LogLayout("CenterInBounds skipped because panel is null");
            return;
        }

        LogLayout("CenterInBounds start");

        if (TryGetCenteredPosition(out Vector2 centeredPosition))
        {
            shouldCenterOnNextLayout = false;
            // The bounds geometry event that follows a successful center can still
            // observe stale world bounds, so ignore that one clamp pass.
            skipNextBoundsClamp = true;
            LogLayout($"CenterInBounds success centered={FormatVector(centeredPosition)}");
            SetPanelPosition(centeredPosition, clampToBounds: true);
            return;
        }

        shouldCenterOnNextLayout = true;
        LogLayout("CenterInBounds deferred until next layout");
    }

    public void ClampToBounds()
    {
        if (panel == null)
        {
            LogLayout("ClampToBounds skipped because panel is null");
            return;
        }

        shouldCenterOnNextLayout = false;
        Vector2 currentPanelPosition = GetCurrentPanelPosition();
        LogLayout($"ClampToBounds using current={FormatVector(currentPanelPosition)}");
        SetPanelPosition(currentPanelPosition, clampToBounds: true);
    }

    private void OnHandlePointerDown(PointerDownEvent evt)
    {
        if (evt.button != (int)MouseButton.LeftMouse || handle == null || panel == null)
        {
            return;
        }

        if (ignoredElement != null && ignoredElement.worldBound.Contains(evt.position))
        {
            LogLayout($"HandlePointerDown ignored because pointer hit ignored element at {FormatVector(evt.position)}");
            return;
        }

        isDragging = true;
        dragPointerId = evt.pointerId;
        dragStartPointerPosition = (Vector2)evt.position;
        dragStartPanelPosition = GetCurrentPanelPosition();
        handle.CapturePointer(dragPointerId);
        LogLayout($"HandlePointerDown captured pointer={dragPointerId} startPointer={FormatVector(dragStartPointerPosition)} startPanel={FormatVector(dragStartPanelPosition)}");
        evt.StopPropagation();
    }

    private void OnHandlePointerMove(PointerMoveEvent evt)
    {
        if (!isDragging || evt.pointerId != dragPointerId)
        {
            return;
        }

        Vector2 delta = (Vector2)evt.position - dragStartPointerPosition;
        LogLayout($"HandlePointerMove pointer={evt.pointerId} delta={FormatVector(delta)}");
        SetPanelPosition(dragStartPanelPosition + delta, clampToBounds: true);
        evt.StopPropagation();
    }

    private void OnHandlePointerUp(PointerUpEvent evt)
    {
        if (!isDragging || evt.pointerId != dragPointerId)
        {
            return;
        }

        LogLayout($"HandlePointerUp pointer={evt.pointerId}");
        StopDragging();
        evt.StopPropagation();
    }

    private void OnHandlePointerCancel(PointerCancelEvent evt)
    {
        if (!isDragging || evt.pointerId != dragPointerId)
        {
            return;
        }

        LogLayout($"HandlePointerCancel pointer={evt.pointerId}");
        StopDragging();
        evt.StopPropagation();
    }

    private void OnBoundsGeometryChanged(GeometryChangedEvent evt)
    {
        LogLayout($"BoundsGeometryChanged old={FormatRect(evt.oldRect)} new={FormatRect(evt.newRect)}");
        if (skipNextBoundsClamp)
        {
            skipNextBoundsClamp = false;
            LogLayout("BoundsGeometryChanged skipped because a center was just applied");
            return;
        }

        if (shouldCenterOnNextLayout)
        {
            CenterInBounds();
            return;
        }

        ClampToBounds();
    }

    private void OnPanelGeometryChanged(GeometryChangedEvent evt)
    {
        LogLayout($"PanelGeometryChanged old={FormatRect(evt.oldRect)} new={FormatRect(evt.newRect)}");
        if (shouldCenterOnNextLayout)
        {
            CenterInBounds();
        }
    }

    private void SetPanelPosition(Vector2 panelPosition, bool clampToBounds)
    {
        if (panel == null)
        {
            return;
        }

        Vector2 targetPosition = clampToBounds ? ClampPanelPosition(panelPosition) : panelPosition;
        LogLayout($"SetPanelPosition input={FormatVector(panelPosition)} target={FormatVector(targetPosition)} clamp={clampToBounds}");
        panel.style.right = StyleKeyword.Auto;
        panel.style.bottom = StyleKeyword.Auto;
        panel.style.left = targetPosition.x;
        panel.style.top = targetPosition.y;
    }

    private Vector2 ClampPanelPosition(Vector2 panelPosition)
    {
        if (boundsRoot == null || panel == null)
        {
            return panelPosition;
        }

        Rect bounds = boundsRoot.worldBound;
        Rect panelBounds = panel.worldBound;
        if (!IsPositiveFinite(bounds.width) ||
            !IsPositiveFinite(bounds.height) ||
            !IsPositiveFinite(panelBounds.width) ||
            !IsPositiveFinite(panelBounds.height))
        {
            LogLayout($"ClampPanelPosition skipped because bounds are not ready bounds={FormatRect(bounds)} panel={FormatRect(panelBounds)}");
            return panelPosition;
        }

        float maxX = Mathf.Max(0f, bounds.width - panelBounds.width);
        float maxY = Mathf.Max(0f, bounds.height - panelBounds.height);
        Vector2 clampedPosition = new Vector2(
            Mathf.Clamp(panelPosition.x, 0f, maxX),
            Mathf.Clamp(panelPosition.y, 0f, maxY));
        LogLayout($"ClampPanelPosition input={FormatVector(panelPosition)} max=({FormatFloat(maxX)},{FormatFloat(maxY)}) output={FormatVector(clampedPosition)}");
        return clampedPosition;
    }

    private Vector2 GetCurrentPanelPosition()
    {
        if (boundsRoot != null && panel != null)
        {
            Rect bounds = boundsRoot.worldBound;
            Rect panelBounds = panel.worldBound;
            if (IsPositiveFinite(bounds.width) &&
                IsPositiveFinite(bounds.height) &&
                IsPositiveFinite(panelBounds.width) &&
                IsPositiveFinite(panelBounds.height))
            {
                Vector2 currentPosition = new Vector2(panelBounds.xMin - bounds.xMin, panelBounds.yMin - bounds.yMin);
                LogLayout($"GetCurrentPanelPosition from world bounds={FormatVector(currentPosition)}");
                return currentPosition;
            }
        }

        if (panel == null)
        {
            LogLayout("GetCurrentPanelPosition fell back to zero because panel is null");
            return Vector2.zero;
        }

        float left = SanitizeCoordinate(panel.resolvedStyle.left);
        float top = SanitizeCoordinate(panel.resolvedStyle.top);
        Vector2 fallbackPosition = new Vector2(left, top);
        LogLayout($"GetCurrentPanelPosition from resolved style={FormatVector(fallbackPosition)}");
        return fallbackPosition;
    }

    private bool TryGetCenteredPosition(out Vector2 centeredPosition)
    {
        centeredPosition = Vector2.zero;
        if (boundsRoot == null || panel == null)
        {
            LogLayout("TryGetCenteredPosition failed because boundsRoot or panel is null");
            return false;
        }

        Rect bounds = boundsRoot.worldBound;
        Rect panelBounds = panel.worldBound;
        if (!IsPositiveFinite(bounds.width) ||
            !IsPositiveFinite(bounds.height) ||
            !IsPositiveFinite(panelBounds.width) ||
            !IsPositiveFinite(panelBounds.height))
        {
            LogLayout($"TryGetCenteredPosition failed because bounds are not ready bounds={FormatRect(bounds)} panel={FormatRect(panelBounds)}");
            return false;
        }

        centeredPosition = new Vector2(
            Mathf.Max(0f, (bounds.width - panelBounds.width) * 0.5f),
            Mathf.Max(0f, (bounds.height - panelBounds.height) * 0.5f));
        LogLayout($"TryGetCenteredPosition computed centered={FormatVector(centeredPosition)}");
        return true;
    }

    private static bool IsPositiveFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
    }

    private static float SanitizeCoordinate(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    private void LogLayout(string message)
    {
        if (!LayoutDebugLogging)
        {
            return;
        }

        Debug.Log($"[DraggableWindow:{GetDebugId()}] {message} | {BuildStateSummary()}");
    }

    private string GetDebugId()
    {
        string panelName = GetElementName(panel);
        string boundsName = GetElementName(boundsRoot);
        return $"{panelName}|{boundsName}";
    }

    private string BuildStateSummary()
    {
        return $"shouldCenter={shouldCenterOnNextLayout} dragging={isDragging} pointerId={dragPointerId} bounds={DescribeElement(boundsRoot)} panel={DescribeElement(panel)} handle={DescribeElement(handle)} ignored={DescribeElement(ignoredElement)}";
    }

    private static string DescribeElement(VisualElement element)
    {
        if (element == null)
        {
            return "<null>";
        }

        IResolvedStyle style = element.resolvedStyle;
        return $"{element.GetType().Name}('{GetElementName(element)}') wb={FormatRect(element.worldBound)} rs=({FormatFloat(style.left)},{FormatFloat(style.top)},{FormatFloat(style.width)},{FormatFloat(style.height)},{style.display})";
    }

    private static string GetElementName(VisualElement element)
    {
        if (element == null)
        {
            return "null";
        }

        return string.IsNullOrWhiteSpace(element.name) ? element.GetType().Name : element.name;
    }

    private static string FormatRect(Rect rect)
    {
        return $"({FormatFloat(rect.x)},{FormatFloat(rect.y)},{FormatFloat(rect.width)},{FormatFloat(rect.height)})";
    }

    private static string FormatVector(Vector2 value)
    {
        return $"({FormatFloat(value.x)},{FormatFloat(value.y)})";
    }

    private static string FormatFloat(float value)
    {
        if (float.IsNaN(value))
        {
            return "NaN";
        }

        if (float.IsPositiveInfinity(value))
        {
            return "+Inf";
        }

        if (float.IsNegativeInfinity(value))
        {
            return "-Inf";
        }

        return value.ToString("0.###");
    }
}
