using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Contracts.Kernel;
using SharpClaw.Contracts.Providers;
using SharpClaw.ModuleSDK;
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

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var caps = new HeuristicCapabilityResolver(ProviderCapabilityHeuristics.ForGoogle);

        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "google-vertex-ai", "Google Vertex AI", false,
            (options, credential) => new GoogleVertexAIApiClient(options.Endpoint, credential), caps,
            parameterSpec: ProviderParameterSpecs.GoogleVertexAI,
            supportsAutomaticEndpointDiscovery: true,
            OwnerId: Identity.Id));

        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "google-gemini", "Google Gemini", false,
            (_, credential) => new GoogleGeminiApiClient(credential), caps,
            parameterSpec: ProviderParameterSpecs.GoogleGemini,
            OwnerId: Identity.Id));
    }
}
