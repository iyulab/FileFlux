using FileFlux.Core;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// <see cref="EdgeType"/> values are stored and sent as numbers, so removing a member must not renumber the rest:
/// <c>SharedEntity</c> (4) was removed because nothing could build it, and its number stays unused.
/// </summary>
public class EdgeTypeOrdinalTests
{
    [Fact]
    public void Ordinals_AreFixed()
    {
        Assert.Equal(0, (int)EdgeType.Sequential);
        Assert.Equal(1, (int)EdgeType.Hierarchical);
        Assert.Equal(2, (int)EdgeType.Reference);
        Assert.Equal(3, (int)EdgeType.Semantic);
        Assert.False(Enum.IsDefined(typeof(EdgeType), 4));
        Assert.Equal(5, (int)EdgeType.SharedStructure);
        Assert.Equal(6, (int)EdgeType.Continuation);
        Assert.Equal(7, (int)EdgeType.Contrast);
        Assert.Equal(8, (int)EdgeType.Example);
        Assert.Equal(9, (int)EdgeType.Definition);
    }
}
