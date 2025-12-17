# Community Implementations

> **FileFlux defines interfaces. The community builds implementations.**

## Overview

FileFlux provides interfaces for AI services but doesn't implement them. This page lists community-contributed implementations that you can use or learn from.

**Why this approach?**
- FileFlux stays lightweight and provider-neutral
- Each organization uses different AI services
- Community implementations are independently maintained and updated
- You have full control over your AI provider choice

---

## How to Use Community Implementations

### Option 1: Install as NuGet Package

```bash
# Example (when available)
dotnet add package FileFlux.Extensions.OpenAI
```

```csharp
using FileFlux.Extensions.OpenAI;

services.AddFileFluxOpenAI(configuration);
services.AddFileFlux();
```

### Option 2: Copy Reference Implementation

Browse the implementation, copy the code, and adapt to your needs.

### Option 3: Link as Git Submodule

```bash
git submodule add <repository-url> lib/fileflux-extensions
```

---

## Available Implementations

### 🔵 OpenAI Integration

**Status:** 🟡 Community Maintained

**Providers:**
- OpenAI API (GPT-4, GPT-3.5-turbo)
- Compatible with OpenAI-compatible APIs (e.g., LMSupply, LM Studio)

**Links:**
- **Repository:** *[Submit your implementation!]*
- **NuGet Package:** *Coming soon*
- **Author:** *Your name here*

**Features:**
- ✅ ITextCompletionService
- ✅ IImageToTextService (GPT-4 Vision)
- ✅ IEmbeddingService (text-embedding-3-small/large)
- ✅ Token usage tracking
- ✅ Cost estimation
- ✅ Retry logic with exponential backoff

**Usage Example:**
```csharp
// When available:
services.AddFileFluxOpenAI(options =>
{
    options.ApiKey = configuration["OpenAI:ApiKey"];
    options.Model = "gpt-4";
    options.EmbeddingModel = "text-embedding-3-small";
});
services.AddFileFlux();
```

---

### 🔷 Azure OpenAI Integration

**Status:** 🟡 Community Maintained

**Providers:**
- Azure OpenAI Service

**Links:**
- **Repository:** *[Submit your implementation!]*
- **NuGet Package:** *Coming soon*
- **Author:** *Your name here*

**Features:**
- ✅ ITextCompletionService
- ✅ IImageToTextService
- ✅ IEmbeddingService
- ✅ Azure AD authentication
- ✅ Managed identity support
- ✅ Regional endpoint support

**Usage Example:**
```csharp
// When available:
services.AddFileFluxAzureOpenAI(options =>
{
    options.Endpoint = configuration["AzureOpenAI:Endpoint"];
    options.ApiKey = configuration["AzureOpenAI:ApiKey"];
    options.DeploymentName = "gpt-4";
});
services.AddFileFlux();
```

---

### 🟣 Anthropic Claude Integration

**Status:** 🟡 Community Maintained

**Providers:**
- Anthropic Claude API (Claude 3 Opus, Sonnet, Haiku)

**Links:**
- **Repository:** *[Submit your implementation!]*
- **NuGet Package:** *Coming soon*
- **Author:** *Your name here*

**Features:**
- ✅ ITextCompletionService
- ✅ IImageToTextService (Claude 3 Vision)
- ✅ Long context support (200K tokens)
- ✅ Streaming responses

**Usage Example:**
```csharp
// When available:
services.AddFileFluxAnthropic(options =>
{
    options.ApiKey = configuration["Anthropic:ApiKey"];
    options.Model = "claude-3-opus-20240229";
});
services.AddFileFlux();
```

---

### 🟢 Local Model Integration

**Status:** 🟡 Community Maintained

**Providers:**
- Ollama
- LM Studio
- llama.cpp
- text-generation-webui

**Links:**
- **Repository:** *[Submit your implementation!]*
- **NuGet Package:** *Coming soon*
- **Author:** *Your name here*

**Features:**
- ✅ ITextCompletionService
- ✅ IEmbeddingService
- ✅ No API costs
- ✅ Full privacy (on-premise)
- ✅ OpenAI-compatible API support

**Usage Example:**
```csharp
// When available:
services.AddFileFluxLocal(options =>
{
    options.Endpoint = "http://localhost:11434"; // Ollama default
    options.Model = "llama3:8b";
});
services.AddFileFlux();
```

---

### 🔴 AWS Bedrock Integration

**Status:** 🟡 Community Maintained

**Providers:**
- AWS Bedrock (Claude, Titan, Jurassic-2)

**Links:**
- **Repository:** *[Submit your implementation!]*
- **NuGet Package:** *Coming soon*
- **Author:** *Your name here*

**Features:**
- ✅ ITextCompletionService
- ✅ IEmbeddingService
- ✅ IAM authentication
- ✅ Multiple model support

**Usage Example:**
```csharp
// When available:
services.AddFileFluxBedrock(options =>
{
    options.Region = "us-east-1";
    options.Model = "anthropic.claude-3-sonnet-20240229-v1:0";
});
services.AddFileFlux();
```

---

### 🟠 Google Gemini Integration

**Status:** 🟡 Community Maintained

**Providers:**
- Google AI Studio
- Google Cloud Vertex AI

**Links:**
- **Repository:** *[Submit your implementation!]*
- **NuGet Package:** *Coming soon*
- **Author:** *Your name here*

**Features:**
- ✅ ITextCompletionService
- ✅ IImageToTextService
- ✅ IEmbeddingService
- ✅ Long context support

**Usage Example:**
```csharp
// When available:
services.AddFileFluxGemini(options =>
{
    options.ApiKey = configuration["Google:ApiKey"];
    options.Model = "gemini-1.5-pro";
});
services.AddFileFlux();
```

---

## Implementation Guidelines

### Creating Your Own Implementation

Want to contribute an implementation? Here's what you need:

#### 1. Implement Required Interfaces

```csharp
public class MyAIService : ITextCompletionService
{
    // Implement all methods from ITextCompletionService
    // See: docs/integration/text-completion-service.md
}
```

#### 2. Follow FileFlux Conventions

- Return valid objects (never null)
- Handle errors gracefully (return empty results, don't throw)
- Respect cancellation tokens
- Track token usage
- Implement IsAvailableAsync() correctly

#### 3. Create Extension Methods

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFileFluxMyProvider(
        this IServiceCollection services,
        Action<MyProviderOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddScoped<ITextCompletionService, MyAIService>();
        return services;
    }
}
```

#### 4. Document Usage

- README with setup instructions
- Configuration examples
- Cost/performance characteristics
- Known limitations

#### 5. Add Tests

- Unit tests with mocks
- Integration tests (optional, with API key)
- Follow FileFlux testing patterns

### Publishing Your Implementation

#### As Separate Package (Recommended)

```
FileFlux.Extensions.YourProvider/
├── src/
│   └── FileFlux.Extensions.YourProvider/
│       ├── Services/
│       │   └── YourProviderService.cs
│       ├── Configuration/
│       │   └── YourProviderOptions.cs
│       └── ServiceCollectionExtensions.cs
├── tests/
│   └── FileFlux.Extensions.YourProvider.Tests/
├── README.md
└── FileFlux.Extensions.YourProvider.csproj
```

**Package Naming:**
- `FileFlux.Extensions.<Provider>` (e.g., `FileFlux.Extensions.OpenAI`)
- Clear provider name in package description
- Tag with "fileflux", "rag", "ai"

#### NuGet Metadata

```xml
<PropertyGroup>
  <PackageId>FileFlux.Extensions.YourProvider</PackageId>
  <Title>FileFlux integration for YourProvider</Title>
  <Description>
    ITextCompletionService implementation for YourProvider AI service.
    Enables FileFlux to use YourProvider for intelligent chunking and quality analysis.
  </Description>
  <PackageTags>fileflux;rag;ai;yourprovider;llm</PackageTags>
  <PackageProjectUrl>https://github.com/yourusername/fileflux-yourprovider</PackageProjectUrl>
  <RepositoryUrl>https://github.com/yourusername/fileflux-yourprovider</RepositoryUrl>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="FileFlux" Version="0.3.*" />
  <!-- Your provider's SDK -->
</ItemGroup>
```

#### Submit to This List

Once published:
1. Open an issue or PR to FileFlux repository
2. Provide: Name, Repository URL, NuGet package name, brief description
3. We'll add your implementation to this page

---

## Reference Implementations

### Learning Resources

**FileFlux Test Project:**
- Location: `tests/FileFlux.Tests/Mocks/MockTextCompletionService.cs`
- Purpose: Understand interface contracts
- Use as: Reference for return types and patterns

**FileFlux Sample Projects:**
- Location: `samples/FileFlux.SampleApp/Services/OpenAiTextCompletionService.cs`
- Purpose: Basic OpenAI integration example (not production-ready)
- Use as: Starting point for your implementation

**Note:** Sample implementations are simplified examples. Production implementations should include:
- Proper error handling
- Retry logic
- Token usage tracking
- Cost monitoring
- Configuration management
- Logging and telemetry

---

## Implementation Status Legend

| Symbol | Status | Meaning |
|--------|--------|---------|
| 🟢 | Official | Maintained by FileFlux team |
| 🟡 | Community | Maintained by community |
| 🔴 | Seeking Maintainer | Implementation exists but needs maintainer |
| ⚪ | Requested | Community wants this, no implementation yet |

---

## Requesting New Implementations

Don't see your AI provider? Request it!

### How to Request

1. **Open GitHub Issue** in FileFlux repository
2. **Title:** `[Community Request] <Provider Name> Integration`
3. **Include:**
   - Provider name and website
   - API documentation link
   - Why you need it
   - (Optional) Offer to help implement

### Most Requested

Current community requests:

| Provider | Votes | Status |
|----------|-------|--------|
| OpenAI | 🌟🌟🌟🌟🌟 | ⚪ Seeking maintainer |
| Azure OpenAI | 🌟🌟🌟🌟 | ⚪ Seeking maintainer |
| Anthropic Claude | 🌟🌟🌟 | ⚪ Seeking maintainer |
| Local Models (Ollama) | 🌟🌟🌟 | ⚪ Seeking maintainer |
| AWS Bedrock | 🌟🌟 | ⚪ Requested |
| Google Gemini | 🌟🌟 | ⚪ Requested |

---

## Contributing

### Ways to Contribute

1. **Create an implementation** for your preferred AI provider
2. **Improve existing implementations** (PRs welcome to community repos)
3. **Write documentation** and usage examples
4. **Test implementations** and report issues
5. **Vote for requested implementations** by commenting on issues

### Community Guidelines

- Be respectful and welcoming
- Share knowledge and help others
- Give credit where it's due
- Follow FileFlux philosophy (interfaces, not implementations)
- Keep implementations independent and maintainable

---

## Getting Help

### For Users

**Need help using an implementation?**
- Check the implementation's repository
- Open issue in that repository
- Ask in FileFlux discussions (tag implementation name)

**FileFlux core issues?**
- Open issue in FileFlux repository
- FileFlux team handles interface changes only

### For Implementers

**Need help building an implementation?**
1. Read [ITextCompletionService Integration Guide](../integration/text-completion-service.md)
2. Study [Mock Implementation](../testing/mock-implementations.md)
3. Check reference implementations above
4. Ask in FileFlux discussions
5. Tag maintainers of similar implementations

---

## Disclaimer

**Important:**
- Community implementations are **independently maintained**
- FileFlux team doesn't endorse or warranty these implementations
- Each implementation has its own license and terms
- Verify security and quality before production use
- Check license compatibility with your project

**FileFlux Commitment:**
- We maintain stable interfaces
- Breaking changes are versioned and documented
- We support the community with guidance
- We link to quality implementations

---

## FAQ

**Q: Why doesn't FileFlux include AI provider implementations?**
A: To stay lightweight, provider-neutral, and avoid dependency bloat. Each organization uses different providers, and this approach gives you full control.

**Q: Can I use multiple implementations simultaneously?**
A: Yes! Register multiple services with different lifetimes or use factory pattern to switch dynamically.

**Q: What if my implementation becomes outdated?**
A: Community implementations are independently versioned. Update when you're ready, or use a different implementation.

**Q: Can I make my implementation private?**
A: Absolutely! You're not required to open-source it. This page is for those who choose to share.

**Q: How do I become an "Official" implementation?**
A: Contact FileFlux maintainers. Official implementations meet strict quality standards and are maintained by the FileFlux team.

**Q: Can I charge for my implementation?**
A: Yes, if your license permits. Some implementations may be commercial products.

---

## Summary

**Community implementations enable:**
- 🎯 **Choice** - Use any AI provider you prefer
- 🚀 **Speed** - Get started quickly with ready-made implementations
- 🤝 **Collaboration** - Learn from and improve community work
- 📦 **Independence** - Update at your own pace

**Getting Started:**
1. Choose an implementation from the list above
2. Install or copy the code
3. Configure with your API credentials
4. Register with FileFlux
5. Start processing documents

**Contributing:**
1. Build your implementation
2. Publish to GitHub and NuGet
3. Submit PR to add to this list
4. Help others in the community

---

**Have an implementation to share?** [Submit a PR](https://github.com/iyulab/FileFlux) or [open an issue](https://github.com/iyulab/FileFlux/issues)!

**Questions?** Ask in [GitHub Discussions](https://github.com/iyulab/FileFlux/discussions)
