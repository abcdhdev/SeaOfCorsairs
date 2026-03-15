using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class PlayerDirectionSpriteController : MonoBehaviour
{
    [SerializeField] private Sprite upRightSprite;
    [SerializeField] private Sprite upLeftSprite;
    [SerializeField] private Sprite downRightSprite;
    [SerializeField] private Sprite downLeftSprite;
    [SerializeField] private bool useXZPlane = true;
    [SerializeField] private bool lockWorldRotation = true;
    [SerializeField] private Vector3 worldEulerRotation = new Vector3(90f, 0f, 0f);

    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void LateUpdate()
    {
        if (lockWorldRotation)
        {
            transform.rotation = Quaternion.Euler(worldEulerRotation);
        }
    }

    public void UpdateSprite(Vector3 direction)
    {
        Vector2 planarDirection = useXZPlane
            ? new Vector2(direction.x, direction.z)
            : new Vector2(direction.x, direction.y);

        if (planarDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        if (planarDirection.x > 0f && planarDirection.y > 0f)
        {
            spriteRenderer.sprite = upRightSprite;
        }
        else if (planarDirection.x < 0f && planarDirection.y > 0f)
        {
            spriteRenderer.sprite = upLeftSprite;
        }
        else if (planarDirection.x > 0f && planarDirection.y < 0f)
        {
            spriteRenderer.sprite = downRightSprite;
        }
        else
        {
            spriteRenderer.sprite = downLeftSprite;
        }
    }
}
