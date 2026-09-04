using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Contracts.Kernel;
using SharpClaw.Contracts.Providers;
using SharpClaw.ModuleSDK;
using SharpClaw.Modules.Providers.OpenAICompatible.Clients;
using SharpClaw.Providers.Common;

namespace SharpClaw.Modules.Providers.OpenAICompatible;

/// <summary>
/// Default module: registers the OpenAI-protocol family of provider plugins
/// (OpenAI, DeepSeek, OpenRouter, Eden AI, ZAI, Vercel AI Gateway, xAI, Groq, Cerebras,
/// Mistral, GitHub Copilot, Minimax, Custom, Google Gemini OpenAI shim,
/// Google Vertex AI OpenAI shim). All fifteen share <see cref="OpenAiCompatibleApiClient"/>
/// as the wire-format base; the heuristics are imported from
/// <see cref="ProviderCapabilityHeuristics"/>.
/// </summary>
public sealed class OpenAICompatibleProvidersModule : ISharpClawModule
{
    public ModuleIdentity Identity { get; } = new(
        "sharpclaw_providers_openai_compat",
        "OpenAI-Compatible Providers",
        "po");

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var openAiCaps  = new HeuristicCapabilityResolver(ProviderCapabilityHeuristics.ForOpenAI);
        var googleCaps  = new HeuristicCapabilityResolver(ProviderCapabilityHeuristics.ForGoogle);
        var deepSeekCaps = new HeuristicCapabilityResolver(ProviderCapabilityHeuristics.ForDeepSeek);
        var edenCaps    = new HeuristicCapabilityResolver(ProviderCapabilityHeuristics.ForEdenAI);
        var mistralCaps = new HeuristicCapabilityResolver(ProviderCapabilityHeuristics.ForMistral);
        var xaiCaps     = new HeuristicCapabilityResolver(ProviderCapabilityHeuristics.ForXai);
        var minimaxCaps = new HeuristicCapabilityResolver(ProviderCapabilityHeuristics.ForMinimax);
        var genericCaps = new HeuristicCapabilityResolver(ProviderCapabilityHeuristics.ForGeneric);

        const string owner = "sharpclaw_providers_openai_compat";

        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "openai", "OpenAI", false,
            (_, credential) => new OpenAiApiClient(credential), openAiCaps,
            parameterSpec: ProviderParameterSpecs.OpenAI,
            costFeedFactory: (_, credential) => new OpenAiApiClient(credential),
            costFeedPermissionDeniedNote:
                "Cost API is available for this provider but the current API key "
                + "lacks the required permissions. OpenAI requires an admin key — "
                + "replace the configured key with an admin key to retrieve cost data.",
            OwnerId: owner));

        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "deepseek", "DeepSeek", false,
            (_, credential) => new DeepSeekApiClient(credential), deepSeekCaps,
            parameterSpec: ProviderParameterSpecs.DeepSeek,
            OwnerId: owner));

        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "openrouter", "OpenRouter", false,
            (_, credential) => new OpenRouterApiClient(credential), genericCaps,
            parameterSpec: ProviderParameterSpecs.OpenRouter,
            OwnerId: owner));

        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "eden-ai", "Eden AI", false,
            (_, credential) => new EdenAIApiClient(credential), edenCaps,
            parameterSpec: ProviderParameterSpecs.EdenAI,
            OwnerId: owner));

        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "google-gemini-openai", "Google Gemini (OpenAI)", false,
            (_, credential) => new GoogleGeminiOpenAiApiClient(credential), googleCaps,
            parameterSpec: ProviderParameterSpecs.GoogleGeminiOpenAi,
            OwnerId: owner));

        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "google-vertex-ai-openai", "Google Vertex AI (OpenAI)", false,
            (_, credential) => new GoogleVertexAIOpenAiApiClient(credential), googleCaps,
            parameterSpec: ProviderParameterSpecs.GoogleVertexAIOpenAi,
            OwnerId: owner));

        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "zai", "Z.AI", false,
            (_, credential) => new ZAIApiClient(credential), genericCaps,
            parameterSpec: ProviderParameterSpecs.ZAI,
            OwnerId: owner));

        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "vercel-ai-gateway", "Vercel AI Gateway", false,
            (_, credential) => new VercelAIGatewayApiClient(credential), genericCaps,
            parameterSpec: ProviderParameterSpecs.VercelAIGateway,
            OwnerId: owner));

        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "xai", "xAI", false,
            (_, credential) => new XAIApiClient(credential), xaiCaps,
            parameterSpec: ProviderParameterSpecs.XAI,
            OwnerId: owner));

        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "groq", "Groq", false,
            (_, credential) => new GroqApiClient(credential), genericCaps,
            parameterSpec: ProviderParameterSpecs.Groq,
            OwnerId: owner));

        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "cerebras", "Cerebras", false,
            (_, credential) => new CerebrasApiClient(credential), genericCaps,
            parameterSpec: ProviderParameterSpecs.Cerebras,
            OwnerId: owner));

        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "mistral", "Mistral", false,
            (_, credential) => new MistralApiClient(credential), mistralCaps,
            parameterSpec: ProviderParameterSpecs.Mistral,
            OwnerId: owner));

        // GitHub Copilot uses a singleton device-code helper and creates
        // provider clients with the saved OAuth token for chat requests.
        var copilotAuth = new GitHubCopilotApiClient();
        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "github-copilot", "GitHub Copilot", false,
            (_, credential) => new GitHubCopilotApiClient(credential), genericCaps,
            parameterSpec: ProviderParameterSpecs.GitHubCopilot,
            deviceCodeFlow: new DeviceCodeAuthClientFlow(copilotAuth),
            OwnerId: owner));

        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "minimax", "MiniMax", false,
            (_, credential) => new MinimaxApiClient(credential), minimaxCaps,
            parameterSpec: ProviderParameterSpecs.Minimax,
            OwnerId: owner));

        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "custom", "Custom (OpenAI-compatible)", true,
            (options, credential) => new CustomOpenAiCompatibleApiClient(options.Endpoint!, credential), genericCaps,
            parameterSpec: ProviderParameterSpecs.Custom,
            isSeedable: false,
            OwnerId: owner));
    }

    // No tools, resources, endpoints, or CLI commands — this module only
    // contributes provider plugins through DI.
}

/// <summary>
/// Adapter exposing an <see cref="IDeviceCodeAuthClient"/> implementation
/// (used internally by <see cref="GitHubCopilotApiClient"/>) through the
/// <see cref="IDeviceCodeFlow"/> plugin contract surfaced by
/// <see cref="IProviderPlugin"/>.
/// </summary>
internal sealed class DeviceCodeAuthClientFlow(IDeviceCodeAuthClient inner) : IDeviceCodeFlow
{
    public Task<SharpClaw.Contracts.DTOs.Providers.DeviceCodeSession> StartAsync(
        CancellationToken ct = default)
        => inner.StartDeviceCodeFlowAsync(ct);

    public async Task<string?> PollAsync(
        SharpClaw.Contracts.DTOs.Providers.DeviceCodeSession session,
        CancellationToken ct = default)
        => await inner.PollForAccessTokenAsync(session, ct);
}
