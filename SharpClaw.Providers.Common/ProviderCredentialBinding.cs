using SharpClaw.Contracts.Providers;

namespace SharpClaw.Providers.Common;

public static class ProviderCredentialBinding
{
    public static IProviderApiClient CreateClient(
        IProviderPlugin plugin,
        ProviderClientOptions options,
        string credential)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(options);

        if (plugin.RequiresApiKey)
        {
            if (string.IsNullOrWhiteSpace(credential))
            {
                throw new InvalidOperationException(
                    $"Provider '{plugin.ProviderKey}' requires credentials, but no credentials are configured.");
            }

            if (plugin is not IProviderCredentialBoundPlugin credentialBound)
            {
                throw new InvalidOperationException(
                    $"Provider '{plugin.ProviderKey}' requires credentials, but its plugin does not support host-side credential binding.");
            }

            return credentialBound.CreateClient(options, credential);
        }

        return plugin.CreateClient(options);
    }

    public static IProviderCostFeed? CreateCostFeed(
        IProviderPlugin plugin,
        ProviderClientOptions options,
        string credential)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(options);

        if (!plugin.SupportsCostFeed)
            return null;

        if (plugin.RequiresApiKey)
        {
            if (string.IsNullOrWhiteSpace(credential))
            {
                throw new InvalidOperationException(
                    $"Provider '{plugin.ProviderKey}' requires credentials, but no credentials are configured.");
            }

            if (plugin is not IProviderCredentialBoundPlugin credentialBound)
            {
                throw new InvalidOperationException(
                    $"Provider '{plugin.ProviderKey}' requires credentials, but its plugin does not support host-side credential binding.");
            }

            return credentialBound.CreateCostFeed(options, credential);
        }

        return plugin.CreateCostFeed(options);
    }
}
