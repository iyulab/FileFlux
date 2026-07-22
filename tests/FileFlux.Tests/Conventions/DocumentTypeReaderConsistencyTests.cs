using FileFlux.Core;
using FileFlux.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Conventions;

/// <summary>
/// Convention teeth: every extension advertised by the DocumentType enum
/// must be handled by a registered reader in the default AddFileFlux() set.
/// Guards against advertised-but-unimplemented drift (e.g. the 0.11.0 .csv gap:
/// DocumentType.Csv existed while no reader handled .csv, so every CSV upload
/// failed with "No reader found").
/// </summary>
public class DocumentTypeReaderConsistencyTests
{
    [Fact]
    public void EveryAdvertisedExtension_ShouldHaveRegisteredReader_DiSet()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddFileFlux();
        using var provider = services.BuildServiceProvider();
        var readers = provider.GetServices<IDocumentReader>().ToList();

        var handledExtensions = readers
            .SelectMany(r => r.SupportedExtensions)
            .Select(e => e.ToLowerInvariant())
            .ToHashSet();

        // Act & Assert
        AssertAllAdvertisedExtensionsHandled(handledExtensions, "AddFileFlux() DI set");
    }

    [Fact]
    public void EveryAdvertisedExtension_ShouldHaveRegisteredReader_DefaultFactory()
    {
        // Arrange - DI-less consumers use the parameterless factory
        var factory = new FileFlux.Infrastructure.Factories.DocumentReaderFactory();

        var handledExtensions = factory.GetSupportedExtensions()
            .Select(e => e.ToLowerInvariant())
            .ToHashSet();

        // Act & Assert
        AssertAllAdvertisedExtensionsHandled(handledExtensions, "DocumentReaderFactory default set");
    }

    private static void AssertAllAdvertisedExtensionsHandled(HashSet<string> handledExtensions, string registrationPath)
    {
        var advertisedExtensions = Enum.GetValues<DocumentType>()
            .Where(t => t != DocumentType.Unknown)
            .SelectMany(t => t.GetExtensions())
            .ToHashSet();

        var unhandled = advertisedExtensions.Except(handledExtensions).ToList();

        Assert.True(unhandled.Count == 0,
            $"DocumentType advertises extensions with no registered reader in {registrationPath}: " +
            $"{string.Join(", ", unhandled)}. Either add a reader or remove the enum mapping - " +
            "advertised-but-unimplemented extensions fail at runtime with 'No reader found'.");
    }
}
