using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using SharpClaw.Contracts.Providers;
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
        using var provider = BuildProvider(module.ConfigureServices);
        var plugin = provider.GetRequiredService<IProviderPlugin>();

        Assert.Multiple(() =>
        {
            Assert.That(module.Id, Is.EqualTo("sharpclaw_providers_anthropic"));
            Assert.That(module.DisplayName, Is.EqualTo("Anthropic Provider"));
            Assert.That(module.ToolPrefix, Is.EqualTo("pa"));
            Assert.That(plugin.ProviderKey, Is.EqualTo("anthropic"));
            Assert.That(plugin.OwnerModuleId, Is.EqualTo(module.Id));
            Assert.That(plugin.ParameterSpec, Is.SameAs(ProviderParameterSpecs.Anthropic));
            Assert.That(module.GetToolDefinitions(), Is.Empty);
        });
    }

    [Test]
    public void GoogleModuleRegistersGeminiAndVertexProviders()
    {
        var module = new GoogleProvidersModule();
        using var provider = BuildProvider(module.ConfigureServices);
        var plugins = provider.GetServices<IProviderPlugin>()
            .ToDictionary(plugin => plugin.ProviderKey);

        Assert.Multiple(() =>
        {
            Assert.That(module.Id, Is.EqualTo("sharpclaw_providers_google"));
            Assert.That(plugins.Keys, Is.EquivalentTo(new[] { "google-gemini", "google-vertex-ai" }));
            Assert.That(plugins.Values.All(plugin => plugin.OwnerModuleId == module.Id), Is.True);
            Assert.That(plugins["google-gemini"].ParameterSpec, Is.SameAs(ProviderParameterSpecs.GoogleGemini));
            Assert.That(plugins["google-vertex-ai"].ParameterSpec, Is.SameAs(ProviderParameterSpecs.GoogleVertexAI));
            Assert.That(plugins["google-vertex-ai"].SupportsAutomaticEndpointDiscovery, Is.True);
            Assert.That(module.GetToolDefinitions(), Is.Empty);
        });
    }

    [Test]
    public void OllamaModuleRegistersEndpointDiscoveredKeylessProvider()
    {
        var module = new OllamaProviderModule();
        using var provider = BuildProvider(module.ConfigureServices);
        var plugin = provider.GetRequiredService<IProviderPlugin>();

        Assert.Multiple(() =>
        {
            Assert.That(module.Id, Is.EqualTo("sharpclaw_providers_ollama"));
            Assert.That(module.ToolPrefix, Is.EqualTo("po2"));
            Assert.That(plugin.ProviderKey, Is.EqualTo("ollama"));
            Assert.That(plugin.OwnerModuleId, Is.EqualTo(module.Id));
            Assert.That(plugin.RequiresApiKey, Is.False);
            Assert.That(plugin.SupportsAutomaticEndpointDiscovery, Is.True);
            Assert.That(plugin.ParameterSpec, Is.SameAs(ProviderParameterSpecs.Ollama));
            Assert.That(module.GetToolDefinitions(), Is.Empty);
        });
    }

    [Test]
    public async Task LlamaSharpModuleRegistersLocalProviderAndLocalModelSurfaces()
    {
        var module = new LlamaSharpProviderModule();
        await using var provider = BuildProvider(module.ConfigureServices);
        var plugin = provider.GetRequiredService<IProviderPlugin>();

        Assert.Multiple(() =>
        {
            Assert.That(module.Id, Is.EqualTo("sharpclaw_providers_llamasharp"));
            Assert.That(module.ToolPrefix, Is.EqualTo("po3"));
            Assert.That(plugin.ProviderKey, Is.EqualTo("llamasharp"));
            Assert.That(plugin.OwnerModuleId, Is.EqualTo(module.Id));
            Assert.That(plugin.RequiresApiKey, Is.False);
            Assert.That(plugin.SupportsCostFeed, Is.True);
            Assert.That(plugin.ParameterSpec, Is.SameAs(ProviderParameterSpecs.LlamaSharp));
            Assert.That(module.GetStorageContracts(), Has.Count.EqualTo(1));
            Assert.That(module.GetStorageContracts()[0].StorageName, Is.EqualTo("local_models"));
            Assert.That(module.GetCliCommands().Single().Name, Is.EqualTo("localmodel"));
            Assert.That(module.GetFrontendContributions().Single().BuilderKey, Is.EqualTo("model-list"));
            Assert.That(module.GetToolDefinitions(), Is.Empty);
        });
    }

    [TestCase("sharpclaw_providers_anthropic", "SharpClaw.Modules.Providers.Anthropic.dll", "SharpClaw.Modules.Providers.Anthropic.AnthropicProviderModule", "0.1.4-beta")]
    [TestCase("sharpclaw_providers_google", "SharpClaw.Modules.Providers.Google.dll", "SharpClaw.Modules.Providers.Google.GoogleProvidersModule", "0.1.4-beta")]
    [TestCase("sharpclaw_providers_ollama", "SharpClaw.Modules.Providers.Ollama.dll", "SharpClaw.Modules.Providers.Ollama.OllamaProviderModule", "0.1.4-beta")]
    [TestCase("sharpclaw_providers_llamasharp", "SharpClaw.Modules.Providers.LlamaSharp.dll", "SharpClaw.Modules.Providers.LlamaSharp.LlamaSharpProviderModule", "0.1.4-beta")]
    public void ModuleManifestCopiesToOutputWithExpectedHostMetadata(
        string moduleId,
        string entryAssembly,
        string moduleType,
        string version)
    {
        var manifestPath = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "modules",
            moduleId,
            "module.json");

        Assert.That(File.Exists(manifestPath), Is.True);

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("id").GetString(), Is.EqualTo(moduleId));
            Assert.That(root.GetProperty("version").GetString(), Is.EqualTo(version));
            Assert.That(root.GetProperty("entryAssembly").GetString(), Is.EqualTo(entryAssembly));
            Assert.That(root.GetProperty("moduleType").GetString(), Is.EqualTo(moduleType));
            Assert.That(root.GetProperty("runtime").GetString(), Is.EqualTo("dotnet"));
            Assert.That(root.GetProperty("hostMode").GetString(), Is.EqualTo("sidecar"));
        });
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddHttpClient();
        configure(services);
        return services.BuildServiceProvider();
    }
}
