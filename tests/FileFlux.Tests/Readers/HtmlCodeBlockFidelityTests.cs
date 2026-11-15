using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.Tests.Readers;

/// <summary>
/// A <c>&lt;pre&gt;</c> block comes out of the HTML reader as a fenced code block with the code as it was written: no line
/// added at its end, and a fence long enough that a fence line inside the code cannot close it.
/// </summary>
public sealed class HtmlCodeBlockFidelityTests
{
    private static async Task<string> ReadAsync(string body)
    {
        var path = Path.Combine(Path.GetTempPath(), "fileflux-htmlcode-" + Guid.NewGuid().ToString("N") + ".html");
        await File.WriteAllTextAsync(path, "<html><body><h1>Guide</h1><p>Intro text.</p>" + body + "<p>Closing words.</p></body></html>", TestContext.Current.CancellationToken);
        try
        {
            var services = new ServiceCollection();
            services.AddFileFlux();
            await using var provider = services.BuildServiceProvider();
            await using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(path);
            await processor.ExtractAsync(cancellationToken: TestContext.Current.CancellationToken);
            return processor.Result.Raw!.Text.ReplaceLineEndings("\n");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("<pre><code>a = 1\nb = 2\n</code></pre>")]
    [InlineData("<pre><code class=\"language-python\">a = 1\nb = 2\n</code></pre>")]
    [InlineData("<pre>a = 1\nb = 2\n</pre>")]
    public async Task A_code_block_that_ends_with_a_line_break_gets_no_empty_line_before_the_closing_fence(string block)
    {
        var text = await ReadAsync(block);

        Assert.Contains("a = 1\nb = 2\n```\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("b = 2\n\n```", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_code_block_without_a_final_line_break_is_unchanged()
    {
        var text = await ReadAsync("<pre><code>a = 1\nb = 2</code></pre>");

        Assert.Contains("```\na = 1\nb = 2\n```\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_code_block_that_holds_a_fence_line_is_written_with_a_longer_fence()
    {
        var text = await ReadAsync("<pre><code>Write:\n```python\nx = 1\n```\ndone\n</code></pre>");

        Assert.Contains("````\nWrite:\n```python\nx = 1\n```\ndone\n````\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Blank_runs_trailing_spaces_and_comment_looking_text_in_code_stay()
    {
        var text = await ReadAsync("<pre><code>def f():\n    x = 1\n\n\n    return x   \n\n&lt;!-- not a marker --&gt;\n# not a heading\n</code></pre>");

        Assert.Contains("def f():\n    x = 1\n\n\n    return x   \n\n<!-- not a marker -->\n# not a heading\n```\n", text, StringComparison.Ordinal);
    }
}
