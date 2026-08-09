using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Contracts.Modules;
using SharpClaw.Contracts.Providers;
using SharpClaw.Modules.Providers.Google.Clients;
using SharpClaw.Providers.Common;

namespace SharpClaw.Modules.Providers.Google;

/// <summary>
/// Default module: registers the native Google provider plugins (Gemini and
/// Vertex AI). Uses Google's <c>generateContent</c> wire format (not the
/// OpenAI-compatible shim — that lives in
/// <c>SharpClaw.Modules.Providers.OpenAICompatible</c>).
/// </summary>
public sealed class GoogleProvidersModule : ISharpClawModule
{
    public ModuleIdentity Identity { get; } = new(
        "sharpclaw_providers_google",
        "Google Native Providers",
        "pg");

    public void Configure(ISharpClawModuleBuilder module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var caps = new HeuristicCapabilityResolver(ProviderCapabilityHeuristics.ForGoogle);

        module.Services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "google-vertex-ai", "Google Vertex AI", false,
            (options, credential) => new GoogleVertexAIApiClient(options.Endpoint, credential), caps,
            parameterSpec: ProviderParameterSpecs.GoogleVertexAI,
            supportsAutomaticEndpointDiscovery: true,
            ownerModuleId: Identity.Id));

        module.Services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "google-gemini", "Google Gemini", false,
            (_, credential) => new GoogleGeminiApiClient(credential), caps,
            parameterSpec: ProviderParameterSpecs.GoogleGemini,
            ownerModuleId: Identity.Id));
    }
}
