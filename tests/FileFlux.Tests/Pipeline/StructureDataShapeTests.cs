using FileFlux.Core;
using FileFlux.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// The data refinement attaches to code, table and list structures is serialized with generated code (it used
/// reflection, which fails in a trimmed, AOT or file-based app); its JSON shape is unchanged.
/// </summary>
public class StructureDataShapeTests
{
    [Fact]
    public async Task Refine_StructureData_KeepsItsJsonShape()
    {
        const string text = "Intro.\n\n```csharp\nvar x = 1;\n```\n\n| A | B |\n| --- | --- |\n| 1 | 2 |\n\n- one\n- two\n- three\n\nOutro.\n";
        var raw = new RawContent { Text = text, File = new SourceFileInfo { Name = "a.md", Extension = ".md" } };

        var refined = await new DocumentRefiner(logger: NullLogger<DocumentRefiner>.Instance)
            .RefineAsync(raw, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(refined.Structures.Count >= 3, "structures: " + string.Join(",", refined.Structures.Select(s => s.Type)) + " | text: " + refined.Text);
        var code = refined.Structures.First(s => s.Type == StructureType.Code).Data;
        Assert.Equal("csharp", code.GetProperty("Language").GetString());

        var table = refined.Structures.First(s => s.Type == StructureType.Table).Data;
        Assert.Equal("1", table[0].GetProperty("A").GetString());

        var list = refined.Structures.First(s => s.Type == StructureType.List).Data;
        Assert.False(list.GetProperty("Ordered").GetBoolean());
        Assert.Equal(3, list.GetProperty("Items").GetArrayLength());
    }
}
