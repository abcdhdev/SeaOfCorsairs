using UnityEngine;
using UnityEngine.UIElements;

public partial class GameUIController
{
    private const int CoordinateRulerSegmentCount = 60;
    private const float CoordinateViewportEpsilon = 0.0001f;
    private const string CoordinateRulerRootName = "CoordinateRulerRoot";
    private const string CoordinateTopLabelsName = "CoordinateTopLabels";
    private const string CoordinateLeftLabelsName = "CoordinateLeftLabels";
    private const string CoordinateRulerLabelClass = "coordinate-ruler-label";
    private const string CoordinateTopLabelClass = "coordinate-top-label";
    private const string CoordinateLeftLabelClass = "coordinate-left-label";
    private const string CoordinateRulerLabelMajorClass = "coordinate-ruler-label-major";

    private VisualElement coordinateRulerRoot;
    private VisualElement coordinateTopLabels;
    private VisualElement coordinateLeftLabels;
    private MinimapHudController coordinateRulerSource;
    private readonly Label[] coordinateTopLabelPool = new Label[CoordinateRulerSegmentCount];
    private readonly Label[] coordinateLeftLabelPool = new Label[CoordinateRulerSegmentCount];
    private Rect coordinateViewportNormalized;
    private bool hasCoordinateViewport;

    private void BindCoordinateRulerElements()
    {
        if (root == null)
        {
            return;
        }

        coordinateRulerRoot = root.Q<VisualElement>(CoordinateRulerRootName);
        coordinateTopLabels = root.Q<VisualElement>(CoordinateTopLabelsName);
        coordinateLeftLabels = root.Q<VisualElement>(CoordinateLeftLabelsName);
        coordinateRulerSource = ResolveCoordinateRulerSource();

        EnsureCoordinateLabelPool(coordinateTopLabels, coordinateTopLabelPool, isVertical: false);
        EnsureCoordinateLabelPool(coordinateLeftLabels, coordinateLeftLabelPool, isVertical: true);
        RefreshCoordinateRuler(forceRefresh: true);
    }

    private void ClearCoordinateRulerReferences()
    {
        coordinateRulerRoot = null;
        coordinateTopLabels = null;
        coordinateLeftLabels = null;
        coordinateRulerSource = null;
        coordinateViewportNormalized = default;
        hasCoordinateViewport = false;
    }

    private void RefreshCoordinateRuler(bool forceRefresh = false)
    {
        if (coordinateTopLabels == null || coordinateLeftLabels == null)
        {
            return;
        }

        if (coordinateRulerSource == null)
        {
            coordinateRulerSource = ResolveCoordinateRulerSource();
        }

        if (!TryGetCoordinateViewportNormalized(out Rect viewportNormalized))
        {
            if (!Application.isPlaying)
            {
                viewportNormalized = new Rect(0f, 0f, 1f, 1f);
            }
            else
            {
                SetCoordinateRulerVisible(false);
                return;
            }
        }

        SetCoordinateRulerVisible(true);

        if (!forceRefresh && hasCoordinateViewport && AreRectsApproximatelyEqual(coordinateViewportNormalized, viewportNormalized))
        {
            return;
        }

        coordinateViewportNormalized = viewportNormalized;
        hasCoordinateViewport = true;

        UpdateHorizontalCoordinateLabels(viewportNormalized.xMin, viewportNormalized.xMax);
        UpdateVerticalCoordinateLabels(viewportNormalized.yMin, viewportNormalized.yMax);
    }

    private bool TryGetCoordinateViewportNormalized(out Rect viewportNormalized)
    {
        viewportNormalized = default;
        return coordinateRulerSource != null &&
               coordinateRulerSource.TryGetViewportMapNormalizedBounds(out viewportNormalized);
    }

    private MinimapHudController ResolveCoordinateRulerSource()
    {
        MinimapHudController localSource = GetComponent<MinimapHudController>();
        if (localSource != null)
        {
            return localSource;
        }

        MinimapHudController[] sources = FindObjectsByType<MinimapHudController>(FindObjectsSortMode.None);
        return sources != null && sources.Length > 0 ? sources[0] : null;
    }

    private static void EnsureCoordinateLabelPool(VisualElement container, Label[] labelPool, bool isVertical)
    {
        if (container == null)
        {
            return;
        }

        for (int i = 0; i < labelPool.Length; i++)
        {
            if (labelPool[i] != null)
            {
                continue;
            }

            Label label = new Label(isVertical ? FormatVerticalCoordinate(i) : FormatHorizontalCoordinate(i + 1))
            {
                pickingMode = PickingMode.Ignore
            };
            label.AddToClassList(CoordinateRulerLabelClass);
            label.AddToClassList(isVertical ? CoordinateLeftLabelClass : CoordinateTopLabelClass);

            if (i == 0 || (i + 1) % 5 == 0)
            {
                label.AddToClassList(CoordinateRulerLabelMajorClass);
            }

            labelPool[i] = label;
            container.Add(label);
        }
    }

    private void UpdateHorizontalCoordinateLabels(float viewportMin, float viewportMax)
    {
        float viewportSpan = Mathf.Max(CoordinateViewportEpsilon, viewportMax - viewportMin);

        for (int i = 0; i < coordinateTopLabelPool.Length; i++)
        {
            Label label = coordinateTopLabelPool[i];
            if (label == null)
            {
                continue;
            }

            float cellMin = i / (float)CoordinateRulerSegmentCount;
            float cellMax = (i + 1) / (float)CoordinateRulerSegmentCount;
            float visibleMin = Mathf.Max(cellMin, viewportMin);
            float visibleMax = Mathf.Min(cellMax, viewportMax);

            if (visibleMax - visibleMin <= CoordinateViewportEpsilon)
            {
                label.style.display = DisplayStyle.None;
                continue;
            }

            float leftPercent = ((visibleMin - viewportMin) / viewportSpan) * 100f;
            float widthPercent = ((visibleMax - visibleMin) / viewportSpan) * 100f;

            label.style.display = DisplayStyle.Flex;
            label.style.left = Length.Percent(leftPercent);
            label.style.width = Length.Percent(widthPercent);
            label.style.borderLeftWidth = Mathf.Abs(visibleMin - cellMin) <= CoordinateViewportEpsilon ? 1f : 0f;
            label.style.borderRightWidth = Mathf.Abs(visibleMax - cellMax) <= CoordinateViewportEpsilon ? 1f : 0f;
        }
    }

    private void UpdateVerticalCoordinateLabels(float viewportMin, float viewportMax)
    {
        float viewportSpan = Mathf.Max(CoordinateViewportEpsilon, viewportMax - viewportMin);

        for (int i = 0; i < coordinateLeftLabelPool.Length; i++)
        {
            Label label = coordinateLeftLabelPool[i];
            if (label == null)
            {
                continue;
            }

            float cellMin = 1f - ((i + 1) / (float)CoordinateRulerSegmentCount);
            float cellMax = 1f - (i / (float)CoordinateRulerSegmentCount);
            float visibleMin = Mathf.Max(cellMin, viewportMin);
            float visibleMax = Mathf.Min(cellMax, viewportMax);

            if (visibleMax - visibleMin <= CoordinateViewportEpsilon)
            {
                label.style.display = DisplayStyle.None;
                continue;
            }

            float topPercent = ((viewportMax - visibleMax) / viewportSpan) * 100f;
            float heightPercent = ((visibleMax - visibleMin) / viewportSpan) * 100f;

            label.style.display = DisplayStyle.Flex;
            label.style.top = Length.Percent(topPercent);
            label.style.height = Length.Percent(heightPercent);
            label.style.borderTopWidth = Mathf.Abs(visibleMax - cellMax) <= CoordinateViewportEpsilon ? 1f : 0f;
            label.style.borderBottomWidth = Mathf.Abs(visibleMin - cellMin) <= CoordinateViewportEpsilon ? 1f : 0f;
        }
    }

    private void SetCoordinateRulerVisible(bool visible)
    {
        DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        if (coordinateRulerRoot != null)
        {
            coordinateRulerRoot.style.display = display;
        }

        if (coordinateTopLabels != null)
        {
            coordinateTopLabels.style.display = display;
        }

        if (coordinateLeftLabels != null)
        {
            coordinateLeftLabels.style.display = display;
        }
    }

    private static bool AreRectsApproximatelyEqual(Rect a, Rect b)
    {
        return Mathf.Abs(a.xMin - b.xMin) <= CoordinateViewportEpsilon &&
               Mathf.Abs(a.xMax - b.xMax) <= CoordinateViewportEpsilon &&
               Mathf.Abs(a.yMin - b.yMin) <= CoordinateViewportEpsilon &&
               Mathf.Abs(a.yMax - b.yMax) <= CoordinateViewportEpsilon;
    }

    private static string FormatHorizontalCoordinate(int index)
    {
        return index.ToString("00");
    }

    private static string FormatVerticalCoordinate(int index)
    {
        int firstLetterIndex = index / 26;
        int secondLetterIndex = index % 26;

        return $"{(char)('A' + firstLetterIndex)}{(char)('A' + secondLetterIndex)}";
    }
}
