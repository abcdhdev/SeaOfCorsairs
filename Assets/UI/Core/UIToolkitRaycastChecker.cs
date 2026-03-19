using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class UIToolkitRaycastChecker
{
    private enum PointerYAxisOrigin
    {
        Unknown = 0,
        BottomLeft = 1,
        TopLeft = 2
    }

    private static HashSet<VisualElement> _blockingElements = new HashSet<VisualElement>();
    private static readonly List<VisualElement> _staleElements = new List<VisualElement>(8);
    private static readonly HashSet<VisualElement> _hoveredBlockingElements = new HashSet<VisualElement>();
    private static readonly List<VisualElement> _staleHoveredElements = new List<VisualElement>(8);
    private static PointerYAxisOrigin _pointerYAxisOrigin = PointerYAxisOrigin.Unknown;

    public static string PointerYAxisOriginDebugName => _pointerYAxisOrigin.ToString();

    public static void RegisterBlockingElement(VisualElement blockingElement)
    {
        if (blockingElement != null)
        {
            if (_blockingElements.Add(blockingElement))
            {
                blockingElement.RegisterCallback<PointerEnterEvent>(OnBlockingPointerEnter);
                blockingElement.RegisterCallback<PointerMoveEvent>(OnBlockingPointerMove);
                blockingElement.RegisterCallback<PointerLeaveEvent>(OnBlockingPointerLeave);
                blockingElement.RegisterCallback<DetachFromPanelEvent>(OnBlockingElementDetached);
            }
        }
    }

    public static void UnregisterBlockingElement(VisualElement blockingElement)
    {
        if (blockingElement != null)
        {
            blockingElement.UnregisterCallback<PointerEnterEvent>(OnBlockingPointerEnter);
            blockingElement.UnregisterCallback<PointerMoveEvent>(OnBlockingPointerMove);
            blockingElement.UnregisterCallback<PointerLeaveEvent>(OnBlockingPointerLeave);
            blockingElement.UnregisterCallback<DetachFromPanelEvent>(OnBlockingElementDetached);
            _blockingElements.Remove(blockingElement);
            _hoveredBlockingElements.Remove(blockingElement);
        }
    }

    public static bool IsBlockingRaycasts(VisualElement element)
    {
        return element != null
               && _blockingElements.Contains(element)
               && element.panel != null
               && element.pickingMode == PickingMode.Position
               && element.visible
               && element.resolvedStyle.display != DisplayStyle.None;
    }

    public static bool IsPointerOverUI()
    {
        if (Pointer.current == null)
        {
            return false;
        }

        return IsPointerOverUI(Pointer.current.position.ReadValue());
    }

    public static bool IsPointerOverUI(Vector2 screenPosition)
    {
        return TryGetBlockingElementAtPointer(screenPosition, out _);
    }

    public static bool TryGetBlockingElementAtPointer(Vector2 screenPosition, out VisualElement blockingElement)
    {
        blockingElement = null;

        if (TryGetHoveredBlockingElement(screenPosition, out blockingElement))
        {
            return true;
        }

        if (_blockingElements.Count == 0)
        {
            return false;
        }

        _staleElements.Clear();

        foreach (VisualElement element in _blockingElements)
        {
            if (element == null)
            {
                _staleElements.Add(element);
                continue;
            }

            if (!IsBlockingRaycasts(element))
            {
                if (element.panel == null)
                {
                    _staleElements.Add(element);
                }

                continue;
            }

            if (ContainsScreenPosition(element, screenPosition))
            {
                blockingElement = element;
                CleanupStaleElements();
                return true;
            }
        }

        CleanupStaleElements();
        return false;
    }

    private static bool ContainsScreenPosition(VisualElement element, Vector2 screenPosition)
    {
        if (element == null || element.panel == null)
        {
            return false;
        }

        if (_pointerYAxisOrigin != PointerYAxisOrigin.Unknown)
        {
            return ContainsScreenPosition(element, screenPosition, _pointerYAxisOrigin);
        }

        bool matchesBottomLeft = ContainsScreenPosition(element, screenPosition, PointerYAxisOrigin.BottomLeft);
        bool matchesTopLeft = ContainsScreenPosition(element, screenPosition, PointerYAxisOrigin.TopLeft);

        if (matchesBottomLeft != matchesTopLeft)
        {
            _pointerYAxisOrigin = matchesBottomLeft
                ? PointerYAxisOrigin.BottomLeft
                : PointerYAxisOrigin.TopLeft;
        }

        return matchesBottomLeft || matchesTopLeft;
    }

    private static bool ContainsScreenPosition(
        VisualElement element,
        Vector2 screenPosition,
        PointerYAxisOrigin pointerYAxisOrigin)
    {
        Vector2 normalizedScreenPosition = NormalizeScreenPositionForPanel(screenPosition, pointerYAxisOrigin);
        Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(element.panel, normalizedScreenPosition);
        Vector2 localPosition = element.WorldToLocal(panelPosition);
        return element.ContainsPoint(localPosition);
    }

    private static Vector2 NormalizeScreenPositionForPanel(
        Vector2 screenPosition,
        PointerYAxisOrigin pointerYAxisOrigin)
    {
        if (pointerYAxisOrigin == PointerYAxisOrigin.TopLeft)
        {
            return new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        }

        return screenPosition;
    }

    private static void OnBlockingPointerEnter(PointerEnterEvent evt)
    {
        if (evt.currentTarget is VisualElement element)
        {
            _hoveredBlockingElements.Add(element);
            TryCalibratePointerYAxisOrigin(element, evt.position);
        }
    }

    private static void OnBlockingPointerMove(PointerMoveEvent evt)
    {
        if (evt.currentTarget is VisualElement element)
        {
            _hoveredBlockingElements.Add(element);
            TryCalibratePointerYAxisOrigin(element, evt.position);
        }
    }

    private static void OnBlockingPointerLeave(PointerLeaveEvent evt)
    {
        if (evt.currentTarget is VisualElement element)
        {
            _hoveredBlockingElements.Remove(element);
        }
    }

    private static void OnBlockingElementDetached(DetachFromPanelEvent evt)
    {
        if (!ReferenceEquals(evt.target, evt.currentTarget))
        {
            return;
        }

        if (evt.currentTarget is VisualElement element)
        {
            _hoveredBlockingElements.Remove(element);
            UnregisterBlockingElement(element);
        }
    }

    private static void TryCalibratePointerYAxisOrigin(VisualElement element, Vector2 panelPointerPosition)
    {
        if (element == null || element.panel == null || Pointer.current == null)
        {
            return;
        }

        Vector2 rawPointerPosition = Pointer.current.position.ReadValue();
        Vector2 rawPanelPosition = RuntimePanelUtils.ScreenToPanel(element.panel, rawPointerPosition);

        Vector2 flippedPointerPosition = new Vector2(rawPointerPosition.x, Screen.height - rawPointerPosition.y);
        Vector2 flippedPanelPosition = RuntimePanelUtils.ScreenToPanel(element.panel, flippedPointerPosition);

        float rawDistance = (rawPanelPosition - panelPointerPosition).sqrMagnitude;
        float flippedDistance = (flippedPanelPosition - panelPointerPosition).sqrMagnitude;

        _pointerYAxisOrigin = rawDistance <= flippedDistance
            ? PointerYAxisOrigin.BottomLeft
            : PointerYAxisOrigin.TopLeft;
    }

    private static void CleanupStaleElements()
    {
        for (int i = 0; i < _staleElements.Count; i++)
        {
            _blockingElements.Remove(_staleElements[i]);
        }

        _staleElements.Clear();
    }

    private static bool TryGetHoveredBlockingElement(Vector2 screenPosition, out VisualElement blockingElement)
    {
        blockingElement = null;

        if (_hoveredBlockingElements.Count == 0)
        {
            return false;
        }

        _staleHoveredElements.Clear();

        foreach (VisualElement element in _hoveredBlockingElements)
        {
            if (!IsBlockingRaycasts(element))
            {
                _staleHoveredElements.Add(element);
                continue;
            }

            if (_pointerYAxisOrigin != PointerYAxisOrigin.Unknown && !ContainsScreenPosition(element, screenPosition))
            {
                _staleHoveredElements.Add(element);
                continue;
            }

            blockingElement = element;
            CleanupStaleHoveredElements();
            return true;
        }

        CleanupStaleHoveredElements();
        return false;
    }

    private static void CleanupStaleHoveredElements()
    {
        for (int i = 0; i < _staleHoveredElements.Count; i++)
        {
            _hoveredBlockingElements.Remove(_staleHoveredElements[i]);
        }

        _staleHoveredElements.Clear();
    }
    
#if UNITY_EDITOR
    //This is used to reset the blocking elements set on playmode enter
    //to fix a bug if you have the quick enter playmode settings turned on
    //and don't unregister all your blocking elements before leaving playmode.
    [InitializeOnEnterPlayMode]
    private static void ResetBlockingElements()
    {
        _blockingElements = new HashSet<VisualElement>();
        _hoveredBlockingElements.Clear();
        _pointerYAxisOrigin = PointerYAxisOrigin.Unknown;
    }
#endif
}
