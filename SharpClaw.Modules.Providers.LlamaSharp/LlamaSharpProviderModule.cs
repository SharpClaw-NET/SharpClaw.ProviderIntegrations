using LLama.Native;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Contracts.Kernel;
using SharpClaw.Contracts.Providers;
using SharpClaw.ModuleSDK;
using SharpClaw.Modules.Providers.LlamaSharp.Clients;
using SharpClaw.Modules.Providers.LlamaSharp.Handlers;
using SharpClaw.Modules.Providers.LlamaSharp.LocalInference;
using SharpClaw.Modules.Providers.LlamaSharp.Services;
using SharpClaw.Providers.Common;
using SharpClaw.Providers.LocalCommon;

namespace SharpClaw.Modules.Providers.LlamaSharp;

/// <summary>
/// Default module: registers the LlamaSharp provider plugin in the host
/// process and owns local-model records through module storage.
/// </summary>
public sealed class LlamaSharpProviderModule : ISharpClawModule
{
    private static int _nativeBackendConfigured;

    public ModuleIdentity Identity { get; } = new(
        "sharpclaw_providers_llamasharp",
        "LlamaSharp Provider",
        "po3");

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // L-015: configure the LLamaSharp native backend exactly once.
        // NativeLibraryConfig is sticky — the first call to a LLama API
        // freezes the backend selection, so this must run before any
        // LocalInferenceProcessManager allocation. Idempotent across
        // hot-reload module reloads.
        if (Interlocked.Exchange(ref _nativeBackendConfigured, 1) == 0)
        {
            NativeLibraryConfig.All
                .WithCuda(true)
                .WithVulkan(true)
                .WithAutoFallback(true)
                .WithLogCallback((level, message) =>
                {
                    if (level >= LLamaLogLevel.Warning)
                        System.Diagnostics.Debug.WriteLine(
                            $"[llama.cpp] {message?.TrimEnd()}", "SharpClaw.CLI");
                });
        }

        services.AddScoped<LocalModelStore>();

        // Process manager (singleton, configured from Local:* keys).
        services.AddSingleton(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var processManager = new LocalInferenceProcessManager();
            if (int.TryParse(cfg["Local:GpuLayerCount"], out var gpuLayers))
                processManager.DefaultGpuLayerCount = gpuLayers;
            if (uint.TryParse(cfg["Local:ContextSize"], out var ctxSize))
                processManager.DefaultContextSize = ctxSize;
            if (int.TryParse(cfg["Local:IdleCooldownMinutes"], out var cooldownMin))
                processManager.IdleCooldown = TimeSpan.FromMinutes(cooldownMin);
            if (bool.TryParse(cfg["Local:KeepLoaded"], out var keepLoaded))
                processManager.KeepLoaded = keepLoaded;
            if (int.TryParse(cfg["Local:MaxCachedSessions"], out var maxCached) && maxCached > 0)
                processManager.MaxCachedSessions = maxCached;
            return processManager;
        });

        // Download / URL resolution helpers (host-agnostic — live in LocalCommon).
        services.AddSingleton<HuggingFaceUrlResolver>();
        services.AddSingleton<ModelDownloadManager>();

        // Module services.
        services.AddScoped<LocalModelService>();
        services.AddScoped<LocalModelEndpointHandler>();
        services.AddScoped<LocalModelLookup>();
        services.AddScoped<ILocalModelFileLookup>(sp => sp.GetRequiredService<LocalModelLookup>());

        // Provider plugin - local LLamaSharp client.
        services.AddSingleton<IProviderPlugin>(sp =>
        {
            var pm = sp.GetRequiredService<LocalInferenceProcessManager>();
            var caps = new HeuristicCapabilityResolver(ProviderCapabilityHeuristics.ForGeneric);
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            return new SimpleProviderPlugin(
                "llamasharp",
                "LlamaSharp (local)",
                requiresEndpoint: false,
                (_, _) => new LocalInferenceApiClient(pm, scopeFactory),
                caps,
                parameterSpec: ProviderParameterSpecs.LlamaSharp,
                costFeedFactory: (_, _) => LocalProviderCostFeed.Instance,
                agentIdentifierSuffix: async (providerName, modelId, ct) =>
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var lookup = scope.ServiceProvider.GetRequiredService<LocalModelLookup>();
                    var sourceUrl = await lookup.GetSourceUrlAsync(modelId, ct);
                    return string.IsNullOrEmpty(sourceUrl)
                        ? providerName.Replace(" ", "-").ToLowerInvariant()
                        : ModelDownloadManager.ResolveSourceFolder(sourceUrl).ToLowerInvariant();
                },
                requiresApiKey: false,
                OwnerId: Identity.Id);
        });

        services.AddStorage(StorageContract());
        foreach (var route in LocalModelEndpointHandler.EndpointRoutes)
            services.AddHttpEndpoint<LocalModelEndpointHandler>(route);
    }

    private ScopedStorageContractDescriptor StorageContract() =>
        new(
            Identity.Id,
            "local_models",
            StorageOperations(),
            "Local GGUF model file records owned by the LlamaSharp provider module.",
            [
                new("modelId", ScopedStorageIndexValueKind.String),
                new("status", ScopedStorageIndexValueKind.String),
                new("sourceUrl", ScopedStorageIndexValueKind.String),
                new("updatedAt", ScopedStorageIndexValueKind.DateTime, AllowsRange: true),
            ],
            MaxDocumentBytes: 131_072,
            MaxBatchSize: 100);

    private static IReadOnlyList<ScopedStorageOperationDescriptor> StorageOperations() =>
    [
        new(ScopedStorageOperations.Get),
        new(ScopedStorageOperations.Upsert),
        new(ScopedStorageOperations.BatchUpsert),
        new(ScopedStorageOperations.Delete),
        new(ScopedStorageOperations.BatchDelete),
        new(ScopedStorageOperations.List),
        new(ScopedStorageOperations.Query),
    ];

    public async Task ShutdownAsync()
    {
        // LocalInferenceProcessManager owns native handles — host DI
        // disposal handles the actual unload.
        await Task.CompletedTask;
    }
}
