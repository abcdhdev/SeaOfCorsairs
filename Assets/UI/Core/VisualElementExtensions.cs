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
    private readonly VisualElement boundsRoot;
    private readonly VisualElement panel;
    private readonly VisualElement handle;
    private readonly VisualElement ignoredElement;

    private bool isDragging;
    private bool shouldCenterOnNextLayout;
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

        if (this.handle != null)
        {
            this.handle.RegisterCallback<PointerDownEvent>(OnHandlePointerDown);
            this.handle.RegisterCallback<PointerMoveEvent>(OnHandlePointerMove);
            this.handle.RegisterCallback<PointerUpEvent>(OnHandlePointerUp);
            this.handle.RegisterCallback<PointerCancelEvent>(OnHandlePointerCancel);
        }
    }

    public bool IsDragging => isDragging;

    public void Dispose()
    {
        StopDragging();

        if (boundsRoot != null)
        {
            boundsRoot.UnregisterCallback<GeometryChangedEvent>(OnBoundsGeometryChanged);
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
        if (handle != null && dragPointerId >= 0 && handle.HasPointerCapture(dragPointerId))
        {
            handle.ReleasePointer(dragPointerId);
        }

        isDragging = false;
        dragPointerId = -1;
        dragStartPointerPosition = default;
        dragStartPanelPosition = default;
    }

    public void CenterInBounds()
    {
        if (panel == null)
        {
            return;
        }

        if (TryGetCenteredPosition(out Vector2 centeredPosition))
        {
            shouldCenterOnNextLayout = false;
            SetPanelPosition(centeredPosition, clampToBounds: true);
            return;
        }

        shouldCenterOnNextLayout = true;
    }

    public void ClampToBounds()
    {
        if (panel == null)
        {
            return;
        }

        shouldCenterOnNextLayout = false;
        SetPanelPosition(GetCurrentPanelPosition(), clampToBounds: true);
    }

    private void OnHandlePointerDown(PointerDownEvent evt)
    {
        if (evt.button != (int)MouseButton.LeftMouse || handle == null || panel == null)
        {
            return;
        }

        if (ignoredElement != null && ignoredElement.worldBound.Contains(evt.position))
        {
            return;
        }

        isDragging = true;
        dragPointerId = evt.pointerId;
        dragStartPointerPosition = (Vector2)evt.position;
        dragStartPanelPosition = GetCurrentPanelPosition();
        handle.CapturePointer(dragPointerId);
        evt.StopPropagation();
    }

    private void OnHandlePointerMove(PointerMoveEvent evt)
    {
        if (!isDragging || evt.pointerId != dragPointerId)
        {
            return;
        }

        Vector2 delta = (Vector2)evt.position - dragStartPointerPosition;
        SetPanelPosition(dragStartPanelPosition + delta, clampToBounds: true);
        evt.StopPropagation();
    }

    private void OnHandlePointerUp(PointerUpEvent evt)
    {
        if (!isDragging || evt.pointerId != dragPointerId)
        {
            return;
        }

        StopDragging();
        evt.StopPropagation();
    }

    private void OnHandlePointerCancel(PointerCancelEvent evt)
    {
        if (!isDragging || evt.pointerId != dragPointerId)
        {
            return;
        }

        StopDragging();
        evt.StopPropagation();
    }

    private void OnBoundsGeometryChanged(GeometryChangedEvent evt)
    {
        if (shouldCenterOnNextLayout)
        {
            CenterInBounds();
            return;
        }

        ClampToBounds();
    }

    private void SetPanelPosition(Vector2 panelPosition, bool clampToBounds)
    {
        if (panel == null)
        {
            return;
        }

        Vector2 targetPosition = clampToBounds ? ClampPanelPosition(panelPosition) : panelPosition;
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
        if (bounds.width <= 0f || bounds.height <= 0f || panelBounds.width <= 0f || panelBounds.height <= 0f)
        {
            return panelPosition;
        }

        float maxX = Mathf.Max(0f, bounds.width - panelBounds.width);
        float maxY = Mathf.Max(0f, bounds.height - panelBounds.height);
        return new Vector2(
            Mathf.Clamp(panelPosition.x, 0f, maxX),
            Mathf.Clamp(panelPosition.y, 0f, maxY));
    }

    private Vector2 GetCurrentPanelPosition()
    {
        if (boundsRoot != null && panel != null)
        {
            Rect bounds = boundsRoot.worldBound;
            Rect panelBounds = panel.worldBound;
            if (bounds.width > 0f && bounds.height > 0f && panelBounds.width > 0f && panelBounds.height > 0f)
            {
                return new Vector2(panelBounds.xMin - bounds.xMin, panelBounds.yMin - bounds.yMin);
            }
        }

        return panel == null
            ? Vector2.zero
            : new Vector2(panel.resolvedStyle.left, panel.resolvedStyle.top);
    }

    private bool TryGetCenteredPosition(out Vector2 centeredPosition)
    {
        centeredPosition = Vector2.zero;
        if (boundsRoot == null || panel == null)
        {
            return false;
        }

        Rect bounds = boundsRoot.worldBound;
        Rect panelBounds = panel.worldBound;
        if (bounds.width <= 0f || bounds.height <= 0f || panelBounds.width <= 0f || panelBounds.height <= 0f)
        {
            return false;
        }

        centeredPosition = new Vector2(
            Mathf.Max(0f, (bounds.width - panelBounds.width) * 0.5f),
            Mathf.Max(0f, (bounds.height - panelBounds.height) * 0.5f));
        return true;
    }
}
