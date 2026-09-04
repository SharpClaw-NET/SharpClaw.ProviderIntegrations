using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using SharpClaw.Contracts.Kernel;
using SharpClaw.Contracts.Providers;
using SharpClaw.ModuleSDK;
using SharpClaw.Modules.Providers.Anthropic;
using SharpClaw.Modules.Providers.Google;
using SharpClaw.Modules.Providers.LlamaSharp;
using SharpClaw.Modules.Providers.Ollama;
using SharpClaw.Providers.Common;

namespace SharpClaw.ProviderIntegrations.Tests;

[TestFixture]
public sealed class RemainingProviderModuleTests
{
    [Test]
    public void AnthropicModuleRegistersNativeAnthropicProvider()
    {
        var module = new AnthropicProviderModule();
        using var provider = BuildProvider(module);
        var plugin = provider.GetRequiredService<IProviderPlugin>();

        Assert.Multiple(() =>
        {
            Assert.That(module.Identity.Id, Is.EqualTo("sharpclaw_providers_anthropic"));
            Assert.That(module.Identity.DisplayName, Is.EqualTo("Anthropic Provider"));
            Assert.That(module.Identity.ToolPrefix, Is.EqualTo("pa"));
            Assert.That(plugin.ProviderKey, Is.EqualTo("anthropic"));
            Assert.That(plugin.OwnerId, Is.EqualTo(module.Identity.Id));
            Assert.That(plugin.ParameterSpec, Is.SameAs(ProviderParameterSpecs.Anthropic));
        });
    }

    [Test]
    public void GoogleModuleRegistersGeminiAndVertexProviders()
    {
        var module = new GoogleProvidersModule();
        using var provider = BuildProvider(module);
        var plugins = provider.GetServices<IProviderPlugin>()
            .ToDictionary(plugin => plugin.ProviderKey);

        Assert.Multiple(() =>
        {
            Assert.That(module.Identity.Id, Is.EqualTo("sharpclaw_providers_google"));
            Assert.That(plugins.Keys, Is.EquivalentTo(new[] { "google-gemini", "google-vertex-ai" }));
            Assert.That(plugins.Values.All(plugin => plugin.OwnerId == module.Identity.Id), Is.True);
            Assert.That(plugins["google-gemini"].ParameterSpec, Is.SameAs(ProviderParameterSpecs.GoogleGemini));
            Assert.That(plugins["google-vertex-ai"].ParameterSpec, Is.SameAs(ProviderParameterSpecs.GoogleVertexAI));
            Assert.That(plugins["google-vertex-ai"].SupportsAutomaticEndpointDiscovery, Is.True);
        });
    }

    [Test]
    public void OllamaModuleRegistersEndpointDiscoveredKeylessProvider()
    {
        var module = new OllamaProviderModule();
        using var provider = BuildProvider(module);
        var plugin = provider.GetRequiredService<IProviderPlugin>();

        Assert.Multiple(() =>
        {
            Assert.That(module.Identity.Id, Is.EqualTo("sharpclaw_providers_ollama"));
            Assert.That(module.Identity.ToolPrefix, Is.EqualTo("po2"));
            Assert.That(plugin.ProviderKey, Is.EqualTo("ollama"));
            Assert.That(plugin.OwnerId, Is.EqualTo(module.Identity.Id));
            Assert.That(plugin.RequiresApiKey, Is.False);
            Assert.That(plugin.SupportsAutomaticEndpointDiscovery, Is.True);
            Assert.That(plugin.ParameterSpec, Is.SameAs(ProviderParameterSpecs.Ollama));
        });
    }

    [Test]
    public async Task LlamaSharpModuleRegistersLocalProviderAndLocalModelSurfaces()
    {
        var module = new LlamaSharpProviderModule();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddHttpClient();
        var graph = ModuleTestBuilder.Configure(module, services);
        await using var provider = services.BuildServiceProvider();
        var plugin = provider.GetRequiredService<IProviderPlugin>();

        Assert.Multiple(() =>
        {
            Assert.That(module.Identity.Id, Is.EqualTo("sharpclaw_providers_llamasharp"));
            Assert.That(module.Identity.ToolPrefix, Is.EqualTo("po3"));
            Assert.That(plugin.ProviderKey, Is.EqualTo("llamasharp"));
            Assert.That(plugin.OwnerId, Is.EqualTo(module.Identity.Id));
            Assert.That(plugin.RequiresApiKey, Is.False);
            Assert.That(plugin.SupportsCostFeed, Is.True);
            Assert.That(plugin.ParameterSpec, Is.SameAs(ProviderParameterSpecs.LlamaSharp));
            Assert.That(graph.Storage, Has.Count.EqualTo(1));
            Assert.That(graph.Storage[0].StorageName, Is.EqualTo("local_models"));
        });
    }

    [TestCase("sharpclaw_providers_anthropic", "SharpClaw.Modules.Providers.Anthropic.dll", "SharpClaw.Modules.Providers.Anthropic.AnthropicProviderModule", "0.5.0-beta.4")]
    [TestCase("sharpclaw_providers_google", "SharpClaw.Modules.Providers.Google.dll", "SharpClaw.Modules.Providers.Google.GoogleProvidersModule", "0.5.0-beta.4")]
    [TestCase("sharpclaw_providers_ollama", "SharpClaw.Modules.Providers.Ollama.dll", "SharpClaw.Modules.Providers.Ollama.OllamaProviderModule", "0.5.0-beta.4")]
    [TestCase("sharpclaw_providers_llamasharp", "SharpClaw.Modules.Providers.LlamaSharp.dll", "SharpClaw.Modules.Providers.LlamaSharp.LlamaSharpProviderModule", "0.5.0-beta.4")]
    public void ModuleManifestCopiesToOutputWithExpectedHostMetadata(
        string SourceId,
        string entryAssembly,
        string entryType,
        string version)
    {
        var manifestPath = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "contributions",
            SourceId,
            "package.json");

        Assert.That(File.Exists(manifestPath), Is.True);

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("id").GetString(), Is.EqualTo(SourceId));
            Assert.That(root.GetProperty("version").GetString(), Is.EqualTo(version));
            Assert.That(root.GetProperty("entryAssembly").GetString(), Is.EqualTo(entryAssembly));
            Assert.That(root.GetProperty("entryType").GetString(), Is.EqualTo(entryType));
            Assert.That(root.GetProperty("runtime").GetString(), Is.EqualTo("dotnet"));
            Assert.That(root.GetProperty("hostMode").GetString(), Is.EqualTo("in-process"));
        });
    }

    private static ServiceProvider BuildProvider(ISharpClawModule module)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddHttpClient();
        ModuleTestBuilder.Configure(module, services);
        return services.BuildServiceProvider();
    }
}
