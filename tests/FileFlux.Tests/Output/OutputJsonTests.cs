using System.Reflection;
using System.Text.Json;
using FileFlux.Domain;
using FileFlux.Infrastructure.Output;

namespace FileFlux.Tests.Output;

public class OutputJsonTests
{
    [Fact]
    public void Metadata_WritesEveryPublicMemberOfDocumentMetadata()
    {
        // The writer lists the members by hand (no reflection at run time); a member added to DocumentMetadata must be
        // added there too, or the chunk files silently stop carrying it.
        var members = typeof(DocumentMetadata).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .Order(StringComparer.Ordinal);

        var written = OutputJson.Metadata(new DocumentMetadata(), JsonNamingPolicy.CamelCase)
            .Select(kv => kv.Key)
            .Order(StringComparer.Ordinal);

        Assert.Equal(members, written);
    }

    [Fact]
    public void ToNode_ConvertsBagValuesByKind()
    {
        var node = OutputJson.ToNode(
            new Dictionary<string, object>
            {
                ["text"] = "a",
                ["flag"] = true,
                ["n"] = 3,
                ["x"] = 0.25,
                ["day"] = DayOfWeek.Monday,
                ["span"] = TimeSpan.FromSeconds(90),
                ["list"] = new List<string> { "p", "q" },
                ["nested"] = new Dictionary<string, object?> { ["k"] = null },
            },
            JsonNamingPolicy.CamelCase)!;

        Assert.Equal("""{"text":"a","flag":true,"n":3,"x":0.25,"day":1,"span":"00:01:30","list":["p","q"],"nested":{"k":null}}""",
            node.ToJsonString());
    }
}
