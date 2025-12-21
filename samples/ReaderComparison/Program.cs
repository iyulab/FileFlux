using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;

namespace ReaderComparison;

class Program
{
    static async Task Main(string[] args)
    {
        var testFile = args.Length > 0
            ? args[0]
            : @"D:\aims-data\테스트_문서\ClusterPlex v5.0_리소스 테스트_Lin.docx";

        if (!File.Exists(testFile))
        {
            Console.WriteLine($"File not found: {testFile}");
            return;
        }

        Console.WriteLine($"Comparing readers for: {Path.GetFileName(testFile)}");
        Console.WriteLine(new string('=', 80));

        // Test 1: Mammoth (WordDocumentReader)
        Console.WriteLine("\n[1] MAMMOTH (WordDocumentReader)");
        Console.WriteLine(new string('-', 40));

        var mammothReader = new WordDocumentReader();
        var mammothResult = await mammothReader.ExtractAsync(testFile);

        Console.WriteLine($"Text Length: {mammothResult.Text.Length:N0} chars");
        Console.WriteLine($"Images: {mammothResult.Images.Count}");
        Console.WriteLine($"Has Tables: {mammothResult.HasTables}");

        var mammothPreview = mammothResult.Text.Length > 2000
            ? mammothResult.Text[..2000] + "\n... (truncated)"
            : mammothResult.Text;

        Console.WriteLine("\n--- Content Preview ---");
        Console.WriteLine(mammothPreview);

        // Save full output
        var outputDir = @"D:\aims-data\test_output_v2\comparison";
        Directory.CreateDirectory(outputDir);
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "mammoth_output.md"),
            mammothResult.Text);

        // Test 2: undoc (OfficeNativeDocumentReader)
        Console.WriteLine("\n\n[2] UNDOC (OfficeNativeDocumentReader)");
        Console.WriteLine(new string('-', 40));

        var undocReader = new OfficeNativeDocumentReader();

        try
        {
            // Check native library status first
            var loader = FileFlux.Core.Infrastructure.Interop.UndocNativeLoader.Instance;
            Console.WriteLine($"Native library loaded: {loader.IsLoaded}");

            if (!loader.IsLoaded)
            {
                Console.WriteLine("Downloading undoc native library...");
                await loader.EnsureLoadedAsync();
                Console.WriteLine($"Native library loaded after download: {loader.IsLoaded}");
                Console.WriteLine($"Library version: {loader.LoadedVersion}");
            }

            // Direct test of undoc native library
            Console.WriteLine("\nDirect native library test:");
            Console.WriteLine($"  Library path: {loader.LibraryPath}");
            Console.WriteLine($"  Version delegate: {(loader.Version != null ? "OK" : "NULL")}");
            Console.WriteLine($"  ParseFile delegate: {(loader.ParseFile != null ? "OK" : "NULL")}");
            Console.WriteLine($"  ToMarkdown delegate: {(loader.ToMarkdown != null ? "OK" : "NULL")}");

            // Try to get library version
            if (loader.Version != null)
            {
                var versionPtr = loader.Version();
                if (versionPtr != IntPtr.Zero)
                {
                    var version = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(versionPtr);
                    Console.WriteLine($"  Native version: {version}");
                }
            }

            var parseFile = loader.ParseFile;
            if (parseFile != null)
            {
                var docHandle = parseFile(testFile);
                Console.WriteLine($"  ParseFile returned: {(docHandle == IntPtr.Zero ? "NULL" : "valid handle")}");

                if (docHandle != IntPtr.Zero)
                {
                    var toMarkdown = loader.ToMarkdown;
                    if (toMarkdown != null)
                    {
                        var mdPtr = toMarkdown(docHandle, 0);
                        Console.WriteLine($"  ToMarkdown returned: {(mdPtr == IntPtr.Zero ? "NULL" : "valid pointer")}");

                        if (mdPtr != IntPtr.Zero)
                        {
                            var md = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(mdPtr);
                            Console.WriteLine($"  Markdown length: {md?.Length ?? 0} chars");
                            loader.FreeString?.Invoke(mdPtr);
                        }
                    }
                    loader.FreeDocument?.Invoke(docHandle);
                }
                else
                {
                    if (loader.LastError != null)
                    {
                        var lastError = loader.LastError.Invoke();
                        if (lastError != IntPtr.Zero)
                        {
                            var error = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(lastError);
                            Console.WriteLine($"  Error: {error}");
                        }
                    }
                }
            }

            var undocResult = await undocReader.ExtractAsync(testFile);

            Console.WriteLine($"\nExtractAsync results:");
            Console.WriteLine($"Text Length: {undocResult.Text.Length:N0} chars");
            Console.WriteLine($"Images: {undocResult.Images.Count}");
            Console.WriteLine($"Has Tables: {undocResult.HasTables}");
            Console.WriteLine($"Warnings: {string.Join(", ", undocResult.Warnings)}");

            if (undocResult.Text.Length == 0)
            {
                Console.WriteLine("WARNING: undoc returned empty text. This may indicate:");
                Console.WriteLine("  - Native library not available for this platform");
                Console.WriteLine("  - Document parsing failed silently");
            }

            var undocPreview = undocResult.Text.Length > 2000
                ? undocResult.Text[..2000] + "\n... (truncated)"
                : undocResult.Text;

            Console.WriteLine("\n--- Content Preview ---");
            Console.WriteLine(undocPreview);

            await File.WriteAllTextAsync(
                Path.Combine(outputDir, "undoc_output.md"),
                undocResult.Text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
        }

        Console.WriteLine("\n\nFull outputs saved to:");
        Console.WriteLine($"  - {Path.Combine(outputDir, "mammoth_output.md")}");
        Console.WriteLine($"  - {Path.Combine(outputDir, "undoc_output.md")}");
    }
}
