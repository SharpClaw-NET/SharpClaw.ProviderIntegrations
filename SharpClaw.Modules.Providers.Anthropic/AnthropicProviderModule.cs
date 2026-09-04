using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Contracts.Kernel;
using SharpClaw.Contracts.Providers;
using SharpClaw.ModuleSDK;
using SharpClaw.Modules.Providers.Anthropic.Clients;
using SharpClaw.Providers.Common;

namespace SharpClaw.Modules.Providers.Anthropic;

/// <summary>
/// Default module: registers the native Anthropic provider plugin.
/// Uses Anthropic's <c>/v1/messages</c> wire format (not OpenAI-compatible).
/// </summary>
public sealed class AnthropicProviderModule : ISharpClawModule
{
    public ModuleIdentity Identity { get; } = new(
        "sharpclaw_providers_anthropic",
        "Anthropic Provider",
        "pa");

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var caps = new HeuristicCapabilityResolver(ProviderCapabilityHeuristics.ForAnthropic);
        services.AddSingleton<IProviderPlugin>(new SimpleProviderPlugin(
            "anthropic", "Anthropic", false,
            (_, credential) => new AnthropicApiClient(credential), caps,
            parameterSpec: ProviderParameterSpecs.Anthropic,
            OwnerId: Identity.Id));
    }
}
