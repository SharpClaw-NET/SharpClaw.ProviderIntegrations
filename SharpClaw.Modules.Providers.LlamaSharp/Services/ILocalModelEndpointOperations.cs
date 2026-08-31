using SharpClaw.Contracts.DTOs.Models;
using SharpClaw.Modules.Providers.LlamaSharp.LocalModels;

namespace SharpClaw.Modules.Providers.LlamaSharp.Services;

internal interface ILocalModelEndpointOperations
{
    Task<ModelResponse> DownloadAndRegisterAsync(
        DownloadModelRequest request,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ResolvedModelFileResponse>> ListAvailableFilesAsync(
        string url,
        CancellationToken ct = default);

    Task<IReadOnlyList<LocalModelFileResponse>> ListLocalModelsAsync(
        CancellationToken ct = default);

    Task LoadModelAsync(
        Guid modelId,
        LoadModelRequest request,
        CancellationToken ct = default);

    Task UnloadModelAsync(Guid modelId, CancellationToken ct = default);

    Task<bool> DeleteLocalModelAsync(Guid modelId, CancellationToken ct = default);

    Task SetMmprojPathAsync(
        Guid modelId,
        string? mmprojPath,
        CancellationToken ct = default);
}
