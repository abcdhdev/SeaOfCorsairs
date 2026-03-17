using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class PlayerDirectionSpriteController : MonoBehaviour
{
    private enum DirectionMode
    {
        FourWayDiagonal = 0,
        EightWay = 1
    }

    [SerializeField] private DirectionMode directionMode = DirectionMode.FourWayDiagonal;
    [SerializeField] private Sprite upSprite;
    [SerializeField] private Sprite upRightSprite;
    [SerializeField] private Sprite rightSprite;
    [SerializeField] private Sprite upLeftSprite;
    [SerializeField] private Sprite downSprite;
    [SerializeField] private Sprite downRightSprite;
    [SerializeField] private Sprite leftSprite;
    [SerializeField] private Sprite downLeftSprite;
    [SerializeField] private Sprite burningUpSprite;
    [SerializeField] private Sprite burningUpRightSprite;
    [SerializeField] private Sprite burningRightSprite;
    [SerializeField] private Sprite burningUpLeftSprite;
    [SerializeField] private Sprite burningDownSprite;
    [SerializeField] private Sprite burningDownRightSprite;
    [SerializeField] private Sprite burningLeftSprite;
    [SerializeField] private Sprite burningDownLeftSprite;
    [SerializeField] private bool useBurningSprites;
    [SerializeField] private bool useXZPlane = true;
    [SerializeField] private bool lockWorldRotation = true;
    [SerializeField] private Vector3 worldEulerRotation = new Vector3(90f, 0f, 0f);

    private SpriteRenderer spriteRenderer;
    private Vector2 lastPlanarDirection;
    private bool hasLastPlanarDirection;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public bool SupportsEightWayMovement => directionMode == DirectionMode.EightWay;

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

        lastPlanarDirection = planarDirection.normalized;
        hasLastPlanarDirection = true;

        Sprite resolvedSprite = ResolveSprite(lastPlanarDirection);
        if (resolvedSprite != null)
        {
            spriteRenderer.sprite = resolvedSprite;
        }
    }

    public void SetBurning(bool isBurning)
    {
        if (useBurningSprites == isBurning)
        {
            return;
        }

        useBurningSprites = isBurning;
        RefreshCurrentSprite();
    }

    private void RefreshCurrentSprite()
    {
        if (!hasLastPlanarDirection)
        {
            return;
        }

        Sprite resolvedSprite = ResolveSprite(lastPlanarDirection);
        if (resolvedSprite != null)
        {
            spriteRenderer.sprite = resolvedSprite;
        }
    }

    private bool HasEightWaySprites(bool burning)
    {
        return GetSprite(DirectionSlot.Up, burning) != null &&
               GetSprite(DirectionSlot.Right, burning) != null &&
               GetSprite(DirectionSlot.Down, burning) != null &&
               GetSprite(DirectionSlot.Left, burning) != null &&
               GetSprite(DirectionSlot.UpRight, burning) != null &&
               GetSprite(DirectionSlot.UpLeft, burning) != null &&
               GetSprite(DirectionSlot.DownRight, burning) != null &&
               GetSprite(DirectionSlot.DownLeft, burning) != null;
    }

    private bool HasFourWayDiagonalSprites(bool burning)
    {
        return GetSprite(DirectionSlot.UpRight, burning) != null &&
               GetSprite(DirectionSlot.UpLeft, burning) != null &&
               GetSprite(DirectionSlot.DownRight, burning) != null &&
               GetSprite(DirectionSlot.DownLeft, burning) != null;
    }

    private Sprite ResolveSprite(Vector2 planarDirection)
    {
        if (useBurningSprites)
        {
            Sprite burningSprite = ResolveSprite(planarDirection, true);
            if (burningSprite != null)
            {
                return burningSprite;
            }
        }

        return ResolveSprite(planarDirection, false);
    }

    private Sprite ResolveSprite(Vector2 planarDirection, bool burning)
    {
        return directionMode switch
        {
            DirectionMode.EightWay => ResolveEightWayOrFallbackSprite(planarDirection, burning),
            _ => ResolveFourWayDiagonalSprite(planarDirection, burning)
        };
    }

    private Sprite ResolveEightWayOrFallbackSprite(Vector2 planarDirection, bool burning)
    {
        if (HasEightWaySprites(burning))
        {
            return ResolveEightWaySprite(planarDirection, burning);
        }

        if (HasFourWayDiagonalSprites(burning))
        {
            return ResolveFourWayDiagonalSprite(planarDirection, burning);
        }

        return null;
    }

    private Sprite ResolveFourWayDiagonalSprite(Vector2 planarDirection, bool burning)
    {
        if (planarDirection.x > 0f && planarDirection.y > 0f)
        {
            return GetSprite(DirectionSlot.UpRight, burning);
        }

        if (planarDirection.x < 0f && planarDirection.y > 0f)
        {
            return GetSprite(DirectionSlot.UpLeft, burning);
        }

        if (planarDirection.x > 0f && planarDirection.y < 0f)
        {
            return GetSprite(DirectionSlot.DownRight, burning);
        }

        return GetSprite(DirectionSlot.DownLeft, burning);
    }

    private Sprite ResolveEightWaySprite(Vector2 planarDirection, bool burning)
    {
        float angle = Mathf.Atan2(planarDirection.y, planarDirection.x) * Mathf.Rad2Deg;
        int sector = Mathf.RoundToInt(angle / 45f);
        sector = ((sector % 8) + 8) % 8;

        return sector switch
        {
            0 => GetSprite(DirectionSlot.Right, burning),
            1 => GetSprite(DirectionSlot.UpRight, burning),
            2 => GetSprite(DirectionSlot.Up, burning),
            3 => GetSprite(DirectionSlot.UpLeft, burning),
            4 => GetSprite(DirectionSlot.Left, burning),
            5 => GetSprite(DirectionSlot.DownLeft, burning),
            6 => GetSprite(DirectionSlot.Down, burning),
            7 => GetSprite(DirectionSlot.DownRight, burning),
            _ => GetSprite(DirectionSlot.DownLeft, burning)
        };
    }

    private Sprite GetSprite(DirectionSlot slot, bool burning)
    {
        return slot switch
        {
            DirectionSlot.Up => burning ? burningUpSprite : upSprite,
            DirectionSlot.UpRight => burning ? burningUpRightSprite : upRightSprite,
            DirectionSlot.Right => burning ? burningRightSprite : rightSprite,
            DirectionSlot.UpLeft => burning ? burningUpLeftSprite : upLeftSprite,
            DirectionSlot.Down => burning ? burningDownSprite : downSprite,
            DirectionSlot.DownRight => burning ? burningDownRightSprite : downRightSprite,
            DirectionSlot.Left => burning ? burningLeftSprite : leftSprite,
            DirectionSlot.DownLeft => burning ? burningDownLeftSprite : downLeftSprite,
            _ => null
        };
    }

    private enum DirectionSlot
    {
        Up,
        UpRight,
        Right,
        UpLeft,
        Down,
        DownRight,
        Left,
        DownLeft
    }
}
