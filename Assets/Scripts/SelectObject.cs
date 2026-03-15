using UnityEngine;

public class SelectObject : MonoBehaviour
{
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
        else
        {
            selectionCircle.transform.position = target.transform.position;
            selectionCircle.transform.rotation = selectionCircleWorldRotation;
            selectionCircle.transform.SetParent(null, true);
        }
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

        // Follow target position only; keep ring orientation fixed in world space.
        selectionCircle.transform.position = selectedTarget.transform.position;
        selectionCircle.transform.rotation = selectionCircleWorldRotation;
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
}
