namespace FileFlux.Core;

/// <summary>
/// Bounding box representing position and size in document.
/// Coordinates are in document units (points for PDF, pixels for images).
/// </summary>
public class BoundingBox
{
    /// <summary>
    /// Left coordinate (X).
    /// </summary>
    public double Left { get; set; }

    /// <summary>
    /// Top coordinate (Y).
    /// </summary>
    public double Top { get; set; }

    /// <summary>
    /// Right coordinate.
    /// </summary>
    public double Right { get; set; }

    /// <summary>
    /// Bottom coordinate.
    /// </summary>
    public double Bottom { get; set; }

    /// <summary>
    /// Width of bounding box.
    /// </summary>
    public double Width => Right - Left;

    /// <summary>
    /// Height of bounding box.
    /// </summary>
    public double Height => Bottom - Top;

    /// <summary>
    /// Center X coordinate.
    /// </summary>
    public double CenterX => (Left + Right) / 2;

    /// <summary>
    /// Center Y coordinate.
    /// </summary>
    public double CenterY => (Top + Bottom) / 2;

    /// <summary>
    /// Area of bounding box.
    /// </summary>
    public double Area => Width * Height;

    /// <summary>
    /// Create bounding box from coordinates.
    /// </summary>
    public static BoundingBox FromCoordinates(double left, double top, double right, double bottom)
    {
        return new BoundingBox
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom
        };
    }

    /// <summary>
    /// Create bounding box from position and size.
    /// </summary>
    public static BoundingBox FromSize(double left, double top, double width, double height)
    {
        return new BoundingBox
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height
        };
    }

    /// <summary>
    /// Check if this box contains a point.
    /// </summary>
    public bool Contains(double x, double y)
    {
        return x >= Left && x <= Right && y >= Top && y <= Bottom;
    }

    /// <summary>
    /// Check if this box intersects with another box.
    /// </summary>
    public bool Intersects(BoundingBox other)
    {
        return Left < other.Right && Right > other.Left &&
               Top < other.Bottom && Bottom > other.Top;
    }

    /// <summary>
    /// Check if this box contains another box entirely.
    /// </summary>
    public bool Contains(BoundingBox other)
    {
        return Left <= other.Left && Right >= other.Right &&
               Top <= other.Top && Bottom >= other.Bottom;
    }

    /// <summary>
    /// Get intersection with another box.
    /// </summary>
    public BoundingBox? Intersection(BoundingBox other)
    {
        if (!Intersects(other))
            return null;

        return new BoundingBox
        {
            Left = Math.Max(Left, other.Left),
            Top = Math.Max(Top, other.Top),
            Right = Math.Min(Right, other.Right),
            Bottom = Math.Min(Bottom, other.Bottom)
        };
    }

    /// <summary>
    /// Get union with another box (smallest box containing both).
    /// </summary>
    public BoundingBox Union(BoundingBox other)
    {
        return new BoundingBox
        {
            Left = Math.Min(Left, other.Left),
            Top = Math.Min(Top, other.Top),
            Right = Math.Max(Right, other.Right),
            Bottom = Math.Max(Bottom, other.Bottom)
        };
    }

    /// <summary>
    /// Expand box by given margin.
    /// </summary>
    public BoundingBox Expand(double margin)
    {
        return new BoundingBox
        {
            Left = Left - margin,
            Top = Top - margin,
            Right = Right + margin,
            Bottom = Bottom + margin
        };
    }

    public override string ToString()
    {
        return $"BoundingBox(L:{Left:F1}, T:{Top:F1}, R:{Right:F1}, B:{Bottom:F1})";
    }
}
