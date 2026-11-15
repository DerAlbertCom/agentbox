namespace Abox.UserSecrets;

public sealed partial class UserSecretsCopy
{
    // Target-specific discovery/rewriting belongs here, after original-value validation.
    // Prepare all target payloads before CopyAsync starts its first write.
    private async Task<IReadOnlyList<UserSecretSnapshot>> PrepareTargetAsync(
        PreparedUserSecrets prepared,
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        await sbx.ValidateUserSecretsDestinationAsync(
            sandbox,
            prepared.Stores.Select(store => store.Id).ToArray(),
            cancellationToken
        );
        return prepared.Stores;
    }
}
