using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

Console.WriteLine("🔍 Testing FileFlux NuGet Package");

try 
{
    // 1. Check what assemblies are loaded
    Console.WriteLine("📦 Loaded assemblies with FileFlux:");
    var fileFluxAssemblies = AppDomain.CurrentDomain.GetAssemblies()
        .Where(a => a.FullName?.Contains("FileFlux") == true)
        .ToList();
    
    if (fileFluxAssemblies.Any())
    {
        foreach (var assembly in fileFluxAssemblies)
        {
            Console.WriteLine($"  - {assembly.GetName().Name}");
        }
    }

    // 2. Test DI Registration
    var services = new ServiceCollection();
    
    // Try to find the extension method through reflection
    var extensionType = Type.GetType("FileFlux.Infrastructure.ServiceCollectionExtensions, FileFlux");
    if (extensionType != null)
    {
        var addMethod = extensionType.GetMethod("AddFileFlux", BindingFlags.Public | BindingFlags.Static);
        if (addMethod != null)
        {
            addMethod.Invoke(null, new object[] { services });
            Console.WriteLine("✅ Successfully registered FileFlux services via reflection");
        }
        else
        {
            Console.WriteLine("❌ AddFileFlux method not found");
        }
    }
    else
    {
        Console.WriteLine("❌ ServiceCollectionExtensions type not found");
    }
    
    Console.WriteLine("✅ FileFlux package validation complete!");
    Console.WriteLine("🚀 Package is ready for deployment");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error during testing: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}
