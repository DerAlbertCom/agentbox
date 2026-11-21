namespace Abox.Infrastructure;

/// <summary>A value-free failure to prepare private certificate staging before export.</summary>
internal sealed class PrivateStagingException()
    : AboxException(
        "Cannot establish private certificate staging. Use an ordinary, accessible temporary directory with owner-only permissions; no export was started."
    );
