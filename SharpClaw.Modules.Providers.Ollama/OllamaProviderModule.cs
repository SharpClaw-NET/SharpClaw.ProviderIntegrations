using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Contracts.Kernel;
using SharpClaw.Contracts.Providers;
using SharpClaw.ModuleSDK;
using SharpClaw.Modules.Providers.Ollama.Clients;
using SharpClaw.Providers.Common;

namespace SharpClaw.Modules.Providers.Ollama;

/// <summary>
/// Default module: registers the Ollama provider plugin (a thin
/// <see cref="OpenAiCompatibleApiClient"/> subclass that targets a
/// user-managed Ollama server and overrides model listing to use
/// Ollama's <c>/api/tags</c> endpoint).
/// </summary>
public sealed class OllamaProviderModule : ISharpClawModule
{
    public ModuleIdentity Identity { get; } = new(
        "sharpclaw_providers_ollama",
        "Ollama Provider",
        "po2");

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var caps = new HeuristicCapabilityResolver(ProviderCapabilityHeuristics.ForGeneric);
        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "ollama", "Ollama (local)", false,
            (options, credential) => new OllamaApiClient(options.Endpoint, credential), caps,
            parameterSpec: ProviderParameterSpecs.Ollama,
            supportsAutomaticEndpointDiscovery: true,
            requiresApiKey: false,
            OwnerId: Identity.Id));
    }
}
