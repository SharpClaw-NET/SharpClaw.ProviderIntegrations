using System.Text.Json;
using NUnit.Framework;
using SharpClaw.Contracts.DTOs.Models;
using SharpClaw.Contracts.Modules;
using SharpClaw.ModuleSDK;
using SharpClaw.Modules.Providers.LlamaSharp;
using SharpClaw.Modules.Providers.LlamaSharp.Handlers;
using SharpClaw.Modules.Providers.LlamaSharp.LocalModels;
using SharpClaw.Modules.Providers.LlamaSharp.Services;

namespace SharpClaw.ProviderIntegrations.Tests;

[TestFixture]
public sealed class LlamaSharpEndpointBoundaryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    [Test]
    public void ModuleGraph_CompilesSevenNeutralLocalModelRoutes()
    {
        var module = new LlamaSharpProviderModule();
        var manifestPath = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "modules",
            module.Identity.Id,
            "module.json");
        var manifest = JsonSerializer.Deserialize<ModuleManifest>(
            File.ReadAllText(manifestPath),
            JsonOptions)!;

        var graph = SharpClawModuleCompiler.Compile(
            module,
            manifest,
            new ModuleCompilationOptions { HostingMode = ModuleHostingMode.InProcess });

        Assert.Multiple(() =>
        {
            Assert.That(graph.Storage.Select(item => item.StorageName),
                Is.EqualTo(["local_models"]));
            Assert.That(graph.Application.Endpoints, Has.Count.EqualTo(7));
            Assert.That(graph.Application.Endpoints.Select(item => item.HandlerType),
                Has.All.EqualTo(typeof(LocalModelEndpointHandler)));
            Assert.That(graph.Application.Endpoints.Select(item => item.Descriptor.Path),
                Is.EquivalentTo(new[]
                {
                    "/models/local/download",
                    "/models/local/download/list",
                    "/models/local/",
                    "/models/local/{modelId}/load",
                    "/models/local/{modelId}/unload",
                    "/models/local/{modelId}",
                    "/models/local/{modelId}/mmproj",
                }));
            Assert.That(graph.Application.Endpoints.Select(item => item.Descriptor.Method),
                Is.EquivalentTo(new[] { "POST", "GET", "GET", "POST", "POST", "DELETE", "PUT" }));
            Assert.That(graph.Application.CliCommands, Is.Empty);
        });
    }

    [Test]
    public async Task Handler_ExecutesAllSevenRoutesWithCanonicalMetadata()
    {
        var modelId = Guid.NewGuid();
        var operations = new RecordingOperations(modelId);
        var handler = new LocalModelEndpointHandler(operations);
        var routes = LocalModelEndpointHandler.EndpointRoutes.ToDictionary(route => route.Id);
        var tokenSource = new CancellationTokenSource();

        var download = await handler.InvokeAsync(
            Request(
                routes["llamasharp.local_models.download"],
                body: JsonSerializer.SerializeToUtf8Bytes(
                    new DownloadModelRequest("https://models.example/model.gguf", ProviderKey: "llamasharp"),
                    JsonOptions)),
            NeverHostActionEntry.Instance,
            tokenSource.Token);
        var available = await handler.InvokeAsync(
            Request(
                routes["llamasharp.local_models.download_list"],
                query: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["url"] = ["https://models.example/repository"],
                }),
            NeverHostActionEntry.Instance,
            tokenSource.Token);
        var list = await handler.InvokeAsync(
            Request(routes["llamasharp.local_models.list"]),
            NeverHostActionEntry.Instance,
            tokenSource.Token);
        var load = await handler.InvokeAsync(
            Request(
                routes["llamasharp.local_models.load"],
                body: JsonSerializer.SerializeToUtf8Bytes(new LoadModelRequest(12, 4096), JsonOptions),
                routeValues: ModelRouteValues(modelId)),
            NeverHostActionEntry.Instance,
            tokenSource.Token);
        var unload = await handler.InvokeAsync(
            Request(
                routes["llamasharp.local_models.unload"],
                routeValues: ModelRouteValues(modelId)),
            NeverHostActionEntry.Instance,
            tokenSource.Token);
        var delete = await handler.InvokeAsync(
            Request(
                routes["llamasharp.local_models.delete"],
                routeValues: ModelRouteValues(modelId)),
            NeverHostActionEntry.Instance,
            tokenSource.Token);
        var mmproj = await handler.InvokeAsync(
            Request(
                routes["llamasharp.local_models.mmproj"],
                body: JsonSerializer.SerializeToUtf8Bytes(new SetMmprojRequest("clip.gguf"), JsonOptions),
                routeValues: ModelRouteValues(modelId)),
            NeverHostActionEntry.Instance,
            tokenSource.Token);

        Assert.Multiple(() =>
        {
            Assert.That(
                new[]
                {
                    download.StatusCode,
                    available.StatusCode,
                    list.StatusCode,
                    load.StatusCode,
                    unload.StatusCode,
                    delete.StatusCode,
                    mmproj.StatusCode,
                },
                Is.EqualTo(new[] { 200, 200, 200, 200, 200, 204, 200 }));
            Assert.That(operations.Calls,
                Is.EqualTo(new[] { "download", "download-list", "list", "load", "unload", "delete", "mmproj" }));
            Assert.That(operations.ModelIds, Has.All.EqualTo(modelId));
            Assert.That(operations.Tokens, Has.All.EqualTo(tokenSource.Token));
            Assert.That(operations.DownloadRequest?.Url,
                Is.EqualTo("https://models.example/model.gguf"));
            Assert.That(operations.DownloadRequest?.ProviderKey, Is.EqualTo("llamasharp"));
            Assert.That(operations.DownloadListUrl,
                Is.EqualTo("https://models.example/repository"));
            Assert.That(operations.LoadRequest, Is.EqualTo(new LoadModelRequest(12, 4096)));
            Assert.That(operations.MmprojPath, Is.EqualTo("clip.gguf"));
        });
    }

    [Test]
    public async Task Handler_RejectsMalformedMetadataBeforeServiceExecution()
    {
        var operations = new RecordingOperations(Guid.NewGuid());
        var handler = new LocalModelEndpointHandler(operations);
        var routes = LocalModelEndpointHandler.EndpointRoutes.ToDictionary(route => route.Id);

        var missingProvider = await handler.InvokeAsync(
            Request(
                routes["llamasharp.local_models.download"],
                body: JsonSerializer.SerializeToUtf8Bytes(
                    new DownloadModelRequest("https://models.example/model.gguf"),
                    JsonOptions)),
            NeverHostActionEntry.Instance,
            CancellationToken.None);
        var missingQuery = await handler.InvokeAsync(
            Request(routes["llamasharp.local_models.download_list"]),
            NeverHostActionEntry.Instance,
            CancellationToken.None);
        var emptyId = await handler.InvokeAsync(
            Request(
                routes["llamasharp.local_models.load"],
                body: JsonSerializer.SerializeToUtf8Bytes(new LoadModelRequest(), JsonOptions),
                routeValues: ModelRouteValues(Guid.Empty)),
            NeverHostActionEntry.Instance,
            CancellationToken.None);
        var noncanonicalId = await handler.InvokeAsync(
            Request(
                routes["llamasharp.local_models.delete"],
                routeValues: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["modelId"] = [Guid.NewGuid().ToString("D").ToUpperInvariant()],
                }),
            NeverHostActionEntry.Instance,
            CancellationToken.None);
        var invalidBody = await handler.InvokeAsync(
            Request(
                routes["llamasharp.local_models.mmproj"],
                body: "{"u8.ToArray(),
                routeValues: ModelRouteValues(Guid.NewGuid())),
            NeverHostActionEntry.Instance,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                new[]
                {
                    missingProvider.StatusCode,
                    missingQuery.StatusCode,
                    emptyId.StatusCode,
                    noncanonicalId.StatusCode,
                    invalidBody.StatusCode,
                },
                Has.All.EqualTo(400));
            Assert.That(operations.Calls, Is.Empty);
        });
    }

    [Test]
    public void Handler_RejectsCancellationBeforeServiceExecution()
    {
        var operations = new RecordingOperations(Guid.NewGuid());
        var handler = new LocalModelEndpointHandler(operations);
        var route = LocalModelEndpointHandler.EndpointRoutes.Single(candidate =>
            candidate.Id == "llamasharp.local_models.list");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await handler.InvokeAsync(
                Request(route),
                NeverHostActionEntry.Instance,
                cancellation.Token));

        Assert.That(operations.Calls, Is.Empty);
    }

    [Test]
    public void Handler_LeavesServiceFailuresOnTheHostFailureBoundary()
    {
        var operations = new RecordingOperations(Guid.NewGuid())
        {
            Failure = new InvalidOperationException("service failed"),
        };
        var handler = new LocalModelEndpointHandler(operations);
        var route = LocalModelEndpointHandler.EndpointRoutes.Single(candidate =>
            candidate.Id == "llamasharp.local_models.list");

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await handler.InvokeAsync(
                Request(route),
                NeverHostActionEntry.Instance,
                CancellationToken.None));

        Assert.That(exception!.Message, Is.EqualTo("service failed"));
    }

    private static HostEndpointRouteRequest Request(
        ModuleEndpointRouteDescriptor descriptor,
        byte[]? body = null,
        IReadOnlyDictionary<string, string[]>? query = null,
        IReadOnlyDictionary<string, string[]>? routeValues = null)
    {
        var context = Context();
        return new HostEndpointRouteRequest(
            new HostEndpointInvocation(context.InvocationId, descriptor.Id, context),
            descriptor.ToRouteIdentity(),
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
            query ?? new Dictionary<string, string[]>(StringComparer.Ordinal),
            body ?? [])
        {
            RouteValues = routeValues ?? new Dictionary<string, string[]>(StringComparer.Ordinal),
        };
    }

    private static HostActionEntryRequestContext Context() =>
        new(
            Guid.NewGuid(),
            "llamasharp-test-capability",
            HostActionEntryIngress.Endpoint,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            RequestPrincipal.Anonymous,
            ExtensionFeatureSet.Empty,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(1),
            DateTimeOffset.UtcNow.AddMinutes(2));

    private static IReadOnlyDictionary<string, string[]> ModelRouteValues(Guid modelId) =>
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["modelId"] = [modelId.ToString("D")],
        };

    private sealed class RecordingOperations(Guid expectedModelId) : ILocalModelEndpointOperations
    {
        public List<string> Calls { get; } = [];
        public List<Guid> ModelIds { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public Exception? Failure { get; init; }
        public DownloadModelRequest? DownloadRequest { get; private set; }
        public string? DownloadListUrl { get; private set; }
        public LoadModelRequest? LoadRequest { get; private set; }
        public string? MmprojPath { get; private set; }

        public Task<ModelResponse> DownloadAndRegisterAsync(
            DownloadModelRequest request,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            Record("download", ct);
            DownloadRequest = request;
            return Task.FromResult(new ModelResponse(
                expectedModelId,
                "test-model",
                Guid.NewGuid(),
                "LlamaSharp"));
        }

        public Task<IReadOnlyList<ResolvedModelFileResponse>> ListAvailableFilesAsync(
            string url,
            CancellationToken ct = default)
        {
            Record("download-list", ct);
            DownloadListUrl = url;
            return Task.FromResult<IReadOnlyList<ResolvedModelFileResponse>>(
                [new("https://models.example/model.gguf", "model.gguf", "Q4")]);
        }

        public Task<IReadOnlyList<LocalModelFileResponse>> ListLocalModelsAsync(
            CancellationToken ct = default)
        {
            Record("list", ct);
            if (Failure is not null)
                return Task.FromException<IReadOnlyList<LocalModelFileResponse>>(Failure);
            return Task.FromResult<IReadOnlyList<LocalModelFileResponse>>(
                [new(
                    Guid.NewGuid(),
                    expectedModelId,
                    "test-model",
                    "https://models.example/model.gguf",
                    "model.gguf",
                    1024,
                    "Q4",
                    LocalModelStatus.Ready,
                    1,
                    false,
                    "llamasharp",
                    null)]);
        }

        public Task LoadModelAsync(
            Guid modelId,
            LoadModelRequest request,
            CancellationToken ct = default)
        {
            Record("load", modelId, ct);
            LoadRequest = request;
            return Task.CompletedTask;
        }

        public Task UnloadModelAsync(Guid modelId, CancellationToken ct = default)
        {
            Record("unload", modelId, ct);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteLocalModelAsync(Guid modelId, CancellationToken ct = default)
        {
            Record("delete", modelId, ct);
            return Task.FromResult(true);
        }

        public Task SetMmprojPathAsync(
            Guid modelId,
            string? mmprojPath,
            CancellationToken ct = default)
        {
            Record("mmproj", modelId, ct);
            MmprojPath = mmprojPath;
            return Task.CompletedTask;
        }

        private void Record(string call, CancellationToken token)
        {
            Calls.Add(call);
            Tokens.Add(token);
        }

        private void Record(string call, Guid modelId, CancellationToken token)
        {
            Calls.Add(call);
            ModelIds.Add(modelId);
            Tokens.Add(token);
        }
    }

    private sealed class NeverHostActionEntry : IHostActionEntry
    {
        public static NeverHostActionEntry Instance { get; } = new();

        public ValueTask<IActionOutcome<TResult>> InvokeAsync<TAction, TResult>(
            HostActionEntryRequest<TAction, TResult> request,
            IHostActionEntryTerminal<TAction, TResult> terminal,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The endpoint handler must not create a second action path.");

        public ValueTask<IActionOutcome<TResult>> InvokeNestedAsync<TParentAction, TAction, TResult>(
            HostActionEntryNestedRequest<TParentAction, TAction, TResult> request,
            IHostActionEntryTerminal<TAction, TResult> terminal,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The endpoint handler must not create a nested action path.");
    }
}
