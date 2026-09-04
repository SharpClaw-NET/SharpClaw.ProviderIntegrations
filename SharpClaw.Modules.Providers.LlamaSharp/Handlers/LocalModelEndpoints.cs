using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using SharpClaw.Contracts.Kernel;
using SharpClaw.Modules.Providers.LlamaSharp.LocalModels;
using SharpClaw.Modules.Providers.LlamaSharp.Services;

namespace SharpClaw.Modules.Providers.LlamaSharp.Handlers;

/// <summary>Executes the LlamaSharp local-model HTTP routes.</summary>
public sealed class LocalModelEndpointHandler : IHttpEndpointHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static IReadOnlyList<RouteDefinition> Routes { get; } =
    [
        Route("llamasharp.local_models.download", "/models/local/download", "POST", RouteOperation.Download),
        Route("llamasharp.local_models.download_list", "/models/local/download/list", "GET", RouteOperation.DownloadList),
        Route("llamasharp.local_models.list", "/models/local/", "GET", RouteOperation.List),
        Route("llamasharp.local_models.load", "/models/local/{modelId}", "POST", RouteOperation.Load, "/load"),
        Route("llamasharp.local_models.unload", "/models/local/{modelId}", "POST", RouteOperation.Unload, "/unload"),
        Route("llamasharp.local_models.delete", "/models/local/{modelId}", "DELETE", RouteOperation.Delete),
        Route("llamasharp.local_models.mmproj", "/models/local/{modelId}", "PUT", RouteOperation.SetMmproj, "/mmproj"),
    ];

    private readonly ILocalModelEndpointOperations _operations;

    public LocalModelEndpointHandler(LocalModelService operations)
        : this((ILocalModelEndpointOperations)operations)
    {
    }

    internal LocalModelEndpointHandler(ILocalModelEndpointOperations operations)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
    }

    public static IReadOnlyList<EndpointRouteDescriptor> EndpointRoutes { get; } =
        Routes.Select(route => route.Descriptor).ToArray();

    public async ValueTask<HttpEndpointResponse> InvokeAsync(
        HostEndpointRouteRequest request,
        IHostActionEntry hostActionEntry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(hostActionEntry);
        cancellationToken.ThrowIfCancellationRequested();

        var route = Routes.SingleOrDefault(candidate =>
            candidate.Descriptor.ToRouteIdentity().Equals(request.Route));
        if (route is null)
            return Error(404, "endpoint_route_not_found");

        return route.Operation switch
        {
            RouteOperation.Download => await DownloadAsync(request, cancellationToken),
            RouteOperation.DownloadList => await ListDownloadsAsync(request, cancellationToken),
            RouteOperation.List => Json(
                200,
                await _operations.ListLocalModelsAsync(cancellationToken)),
            RouteOperation.Load => await LoadAsync(request, cancellationToken),
            RouteOperation.Unload => await UnloadAsync(request, cancellationToken),
            RouteOperation.Delete => await DeleteAsync(request, cancellationToken),
            RouteOperation.SetMmproj => await SetMmprojAsync(request, cancellationToken),
            _ => throw new InvalidOperationException("The local-model endpoint operation is invalid."),
        };
    }

    private async ValueTask<HttpEndpointResponse> DownloadAsync(
        HostEndpointRouteRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryReadBody(request.Body, out DownloadModelRequest? payload) ||
            string.IsNullOrWhiteSpace(payload.Url) ||
            string.IsNullOrWhiteSpace(payload.ProviderKey))
        {
            return Error(400, "endpoint_invalid_request");
        }

        var result = await _operations.DownloadAndRegisterAsync(
            payload,
            progress: null,
            cancellationToken);
        return Json(200, result);
    }

    private async ValueTask<HttpEndpointResponse> ListDownloadsAsync(
        HostEndpointRouteRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryReadSingleValue(request.Query, "url", out var url) ||
            string.IsNullOrWhiteSpace(url))
        {
            return Error(400, "endpoint_invalid_request");
        }

        return Json(
            200,
            await _operations.ListAvailableFilesAsync(url, cancellationToken));
    }

    private async ValueTask<HttpEndpointResponse> LoadAsync(
        HostEndpointRouteRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryReadModelId(request.RouteValues, out var modelId) ||
            !TryReadBody(request.Body, out LoadModelRequest? payload))
        {
            return Error(400, "endpoint_invalid_request");
        }

        await _operations.LoadModelAsync(modelId, payload, cancellationToken);
        return Json(200, new { modelId, pinned = true });
    }

    private async ValueTask<HttpEndpointResponse> UnloadAsync(
        HostEndpointRouteRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryReadModelId(request.RouteValues, out var modelId))
            return Error(400, "endpoint_invalid_request");

        await _operations.UnloadModelAsync(modelId, cancellationToken);
        return HttpEndpointResponse.Empty(200);
    }

    private async ValueTask<HttpEndpointResponse> DeleteAsync(
        HostEndpointRouteRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryReadModelId(request.RouteValues, out var modelId))
            return Error(400, "endpoint_invalid_request");

        return await _operations.DeleteLocalModelAsync(modelId, cancellationToken)
            ? HttpEndpointResponse.Empty(204)
            : HttpEndpointResponse.Empty(404);
    }

    private async ValueTask<HttpEndpointResponse> SetMmprojAsync(
        HostEndpointRouteRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryReadModelId(request.RouteValues, out var modelId) ||
            !TryReadBody(request.Body, out SetMmprojRequest? payload))
        {
            return Error(400, "endpoint_invalid_request");
        }

        await _operations.SetMmprojPathAsync(modelId, payload.MmprojPath, cancellationToken);
        return Json(200, new { modelId, mmprojPath = payload.MmprojPath });
    }

    private static bool TryReadBody<T>(byte[] body, [NotNullWhen(true)] out T? value)
        where T : class
    {
        value = default;
        if (body is null || body.Length == 0)
            return false;

        try
        {
            value = JsonSerializer.Deserialize<T>(body, JsonOptions);
            return value is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadModelId(
        IReadOnlyDictionary<string, string[]> routeValues,
        out Guid modelId)
    {
        modelId = default;
        return TryReadSingleValue(routeValues, "modelId", out var value) &&
               Guid.TryParseExact(value, "D", out modelId) &&
               modelId != Guid.Empty &&
               string.Equals(value, modelId.ToString("D"), StringComparison.Ordinal);
    }

    private static bool TryReadSingleValue(
        IReadOnlyDictionary<string, string[]> values,
        string key,
        out string value)
    {
        value = string.Empty;
        if (values is null ||
            !values.TryGetValue(key, out var candidates) ||
            candidates is null ||
            candidates.Length != 1 ||
            string.IsNullOrWhiteSpace(candidates[0]))
        {
            return false;
        }

        value = candidates[0];
        return true;
    }

    private static RouteDefinition Route(
        string id,
        string pathPrefix,
        string method,
        RouteOperation operation,
        string suffix = "") =>
        new(
            new EndpointRouteDescriptor(
                id,
                pathPrefix + suffix,
                method,
                HostEndpointTransport.Http),
            operation);

    private static HttpEndpointResponse Json<T>(int statusCode, T value) =>
        HttpEndpointResponse.Json(
            statusCode,
            JsonSerializer.SerializeToElement(value, JsonOptions));

    private static HttpEndpointResponse Error(int statusCode, string code) =>
        Json(statusCode, new { error = code });

    private sealed record RouteDefinition(
        EndpointRouteDescriptor Descriptor,
        RouteOperation Operation);

    private enum RouteOperation
    {
        Download,
        DownloadList,
        List,
        Load,
        Unload,
        Delete,
        SetMmproj,
    }
}
