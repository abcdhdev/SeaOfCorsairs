using UnityEngine;

public class SelectObject : MonoBehaviour
{
    private const float MinimumSelectionCircleScale = 1f;
    private const float SelectionCirclePadding = 2.5f;
    private const float TurretSelectionCircleMultiplier = 1.35f;

    public static SelectObject Instance; // Singleton

    private GameObject selectedTarget;
    public GameObject SelectedTarget => selectedTarget;
    public GameObject selectedNPC => selectedTarget; // Backward compatibility.
    public GameObject selectionCirclePrefab;  // Set this in the Inspector
    private GameObject selectionCircle;
    private Quaternion selectionCircleWorldRotation = Quaternion.identity;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void Select(GameObject target)
    {
        if (target == null)
        {
            Deselect();
            return;
        }

        if (selectedTarget == target)
        {
            return;
        }

        if (selectedTarget != null)
        {
            Deselect();
        }

        selectedTarget = target;

        if (selectionCircle == null)
        {
            selectionCircle = Instantiate(selectionCirclePrefab, target.transform.position, selectionCirclePrefab.transform.rotation);
            selectionCircleWorldRotation = selectionCircle.transform.rotation;
        }

        UpdateSelectionCircleTransform(target);
        selectionCircle.SetActive(true);
    }

    private void LateUpdate()
    {
        if (selectionCircle == null || !selectionCircle.activeSelf)
        {
            return;
        }

        if (selectedTarget == null)
        {
            Deselect();
            return;
        }

        UpdateSelectionCircleTransform(selectedTarget);
    }

    public void Deselect()
    {
        if (selectionCircle)
        {
            selectionCircle.transform.SetParent(null, true);
            selectionCircle.SetActive(false);
        }

        selectedTarget = null;
    }

    private void UpdateSelectionCircleTransform(GameObject target)
    {
        if (selectionCircle == null || target == null)
        {
            return;
        }

        // Keep the ring centered on the target and scale it from the target footprint.
        selectionCircle.transform.SetParent(null, true);
        selectionCircle.transform.position = target.transform.position;
        selectionCircle.transform.rotation = selectionCircleWorldRotation;

        float selectionScale = Mathf.Max(
            MinimumSelectionCircleScale,
            CombatTargetingUtility.GetSelectionRadius(target, SelectionCirclePadding));

        if (target.TryGetComponent(out IslandTurret _))
        {
            selectionScale *= TurretSelectionCircleMultiplier;
        }

        selectionCircle.transform.localScale = Vector3.one * selectionScale;
    }
}
