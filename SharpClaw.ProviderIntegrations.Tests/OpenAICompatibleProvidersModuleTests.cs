using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using SharpClaw.Contracts.Providers;
using SharpClaw.Modules.Providers.OpenAICompatible;

namespace SharpClaw.ProviderIntegrations.Tests;

[TestFixture]
public sealed class OpenAICompatibleProvidersModuleTests
{
    [Test]
    public void ConfigureRegistersOpenAICompatibleProviderFamily()
    {
        var module = new OpenAICompatibleProvidersModule();
        var services = new ServiceCollection();

        module.Configure(ModuleTestBuilder.For(services));

        using var provider = services.BuildServiceProvider();
        var plugins = provider.GetServices<IProviderPlugin>().ToList();
        var providerKeys = plugins.Select(plugin => plugin.ProviderKey).ToArray();

        var expectedKeys = new[]
        {
            "openai",
            "deepseek",
            "openrouter",
            "eden-ai",
            "google-gemini-openai",
            "google-vertex-ai-openai",
            "zai",
            "vercel-ai-gateway",
            "xai",
            "groq",
            "cerebras",
            "mistral",
            "github-copilot",
            "minimax",
            "custom",
        };

        Assert.Multiple(() =>
        {
            Assert.That(module.Identity.Id, Is.EqualTo("sharpclaw_providers_openai_compat"));
            Assert.That(module.Identity.DisplayName, Is.EqualTo("OpenAI-Compatible Providers"));
            Assert.That(module.Identity.ToolPrefix, Is.EqualTo("po"));
            Assert.That(plugins, Has.Count.EqualTo(expectedKeys.Length));
            Assert.That(providerKeys, Is.EquivalentTo(expectedKeys));
            Assert.That(plugins.All(plugin => plugin.OwnerModuleId == module.Identity.Id), Is.True);
        });
    }

    [Test]
    public void ConfigurePreservesSpecialProviderSettings()
    {
        var module = new OpenAICompatibleProvidersModule();
        var services = new ServiceCollection();

        module.Configure(ModuleTestBuilder.For(services));

        using var provider = services.BuildServiceProvider();
        var plugins = provider.GetServices<IProviderPlugin>().ToDictionary(plugin => plugin.ProviderKey);

        Assert.Multiple(() =>
        {
            Assert.That(plugins["custom"].RequiresEndpoint, Is.True);
            Assert.That(plugins["custom"].IsSeedable, Is.False);
            Assert.That(plugins["openai"].SupportsCostFeed, Is.True);
            Assert.That(plugins["github-copilot"].DeviceCodeFlow, Is.Not.Null);
        });
    }

    [Test]
    public void ModuleManifestCopiesToOutputWithExpectedHostMetadata()
    {
        var manifestPath = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "modules",
            "sharpclaw_providers_openai_compat",
            "module.json");

        Assert.That(File.Exists(manifestPath), Is.True);

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("id").GetString(), Is.EqualTo("sharpclaw_providers_openai_compat"));
            Assert.That(root.GetProperty("entryAssembly").GetString(), Is.EqualTo("SharpClaw.Modules.Providers.OpenAICompatible.dll"));
            Assert.That(root.GetProperty("moduleType").GetString(), Is.EqualTo("SharpClaw.Modules.Providers.OpenAICompatible.OpenAICompatibleProvidersModule"));
            Assert.That(root.GetProperty("runtime").GetString(), Is.EqualTo("dotnet"));
            Assert.That(root.GetProperty("hostMode").GetString(), Is.EqualTo("in-process"));
        });
    }
}
