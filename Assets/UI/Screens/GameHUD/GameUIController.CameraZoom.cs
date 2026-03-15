using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public partial class GameUIController
{
    private void InitializeCameraZoomControl()
    {
        displayedCameraZoom = float.NaN;

        if (cameraZoomSlider == null)
        {
            return;
        }

        cameraZoomSlider.direction = SliderDirection.Vertical;
        cameraZoomSlider.inverted = true;
        cameraZoomSlider.pageSize = 1f;
        cameraZoomSlider.focusable = false;
        cameraZoomSlider.delegatesFocus = false;
        cameraZoomSlider.tabIndex = -1;

        if (cameraZoomRoot != null)
        {
            cameraZoomRoot.focusable = false;
            cameraZoomRoot.delegatesFocus = false;
        }

        UpdateCameraZoomControl();
    }

    private void UpdateCameraZoomControl()
    {
        if (cameraZoomSlider == null)
        {
            return;
        }

        if (cameraController == null)
        {
            cameraController = FindFirstObjectByType<IsometricCameraController>();
        }

        if (cameraController == null)
        {
            cameraZoomSlider.SetEnabled(false);
            cameraZoomSlider.tooltip = "Assign a camera controller.";
            UpdateCameraZoomThumb(cameraZoomSlider.lowValue);
            return;
        }

        float minZoom = cameraController.MinZoom;
        float maxZoom = cameraController.MaxZoom;
        cameraZoomSlider.lowValue = minZoom;
        cameraZoomSlider.highValue = maxZoom;

        bool supportsZoom = cameraController.SupportsZoom();
        cameraZoomSlider.SetEnabled(supportsZoom);
        cameraZoomSlider.tooltip = supportsZoom ? "Camera zoom" : "Perspective camera required.";
        if (!supportsZoom)
        {
            displayedCameraZoom = float.NaN;
            UpdateCameraZoomThumb(cameraZoomSlider.lowValue);
            return;
        }

        float currentZoom = Mathf.Clamp(cameraController.GetZoom(), minZoom, maxZoom);
        if (float.IsNaN(displayedCameraZoom) || !Mathf.Approximately(displayedCameraZoom, currentZoom))
        {
            displayedCameraZoom = currentZoom;
            cameraZoomSlider.SetValueWithoutNotify(currentZoom);
        }

        UpdateCameraZoomThumb(currentZoom);
    }

    private void OnCameraZoomChanged(ChangeEvent<float> evt)
    {
        if (cameraController == null)
        {
            return;
        }

        cameraController.SetZoom(evt.newValue);
        displayedCameraZoom = cameraController.GetZoom();
        if (cameraZoomSlider != null && !Mathf.Approximately(displayedCameraZoom, evt.newValue))
        {
            cameraZoomSlider.SetValueWithoutNotify(displayedCameraZoom);
        }

        UpdateCameraZoomThumb(displayedCameraZoom);
    }

    private void OnCameraZoomGeometryChanged(GeometryChangedEvent evt)
    {
        if (cameraZoomSlider == null)
        {
            return;
        }

        float value = float.IsNaN(displayedCameraZoom) ? cameraZoomSlider.value : displayedCameraZoom;
        UpdateCameraZoomThumb(value);
    }

    private void UpdateCameraZoomThumb(float zoomValue)
    {
        if (cameraZoomRoot == null || cameraZoomSlider == null || cameraZoomThumb == null)
        {
            return;
        }

        float rootWidth = cameraZoomRoot.resolvedStyle.width;
        float rootHeight = cameraZoomRoot.resolvedStyle.height;
        float sliderWidth = cameraZoomSlider.resolvedStyle.width;
        float sliderHeight = cameraZoomSlider.resolvedStyle.height;
        float thumbWidth = cameraZoomThumb.resolvedStyle.width;
        float thumbHeight = cameraZoomThumb.resolvedStyle.height;

        if (rootWidth <= 0f || rootHeight <= 0f || sliderWidth <= 0f || sliderHeight <= 0f || thumbWidth <= 0f || thumbHeight <= 0f)
        {
            return;
        }

        float minZoom = cameraZoomSlider.lowValue;
        float maxZoom = cameraZoomSlider.highValue;
        float clampedZoom = Mathf.Clamp(zoomValue, minZoom, maxZoom);
        float normalized = Mathf.InverseLerp(minZoom, maxZoom, clampedZoom);

        float sliderTop = (rootHeight - sliderHeight) * 0.5f;
        float sliderLeft = (rootWidth - sliderWidth) * 0.5f;
        float travel = Mathf.Max(0f, sliderHeight - thumbHeight);

        cameraZoomThumb.style.top = sliderTop + (normalized * travel);
        cameraZoomThumb.style.left = sliderLeft + ((sliderWidth - thumbWidth) * 0.5f);
    }

    private void OnCenterCameraClicked()
    {
        if (cameraController != null)
        {
            cameraController.CenterOnTarget();
        }
    }

    private void OnCenterCameraAction(InputAction.CallbackContext context)
    {
        OnCenterCameraClicked();
    }
}
