using Microsoft.Extensions.DependencyInjection;
using SharpClaw.ModuleSDK;

namespace SharpClaw.ProviderIntegrations.Tests;

internal static class ModuleTestBuilder
{
    public static ModuleContributionGraph Configure(
        ISharpClawModule module,
        IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(services);

        var graph = SharpClawModuleCompiler.Compile(
            module,
            options: new ModuleCompilationOptions
            {
                HostingMode = ModuleHostingMode.OutOfProcess,
                RequireManifestRequests = false,
            });

        foreach (var descriptor in graph.Services)
            ((ICollection<ServiceDescriptor>)services).Add(descriptor);

        return graph;
    }
}
