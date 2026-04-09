public enum MapTransitionDirection
{
    North = 0,
    East = 1,
    South = 2,
    West = 3,
}

public static class MapTransitionDirectionExtensions
{
    public static MapTransitionDirection GetOpposite(this MapTransitionDirection direction)
    {
        return direction switch
        {
            MapTransitionDirection.North => MapTransitionDirection.South,
            MapTransitionDirection.East => MapTransitionDirection.West,
            MapTransitionDirection.South => MapTransitionDirection.North,
            MapTransitionDirection.West => MapTransitionDirection.East,
            _ => direction,
        };
    }
}
