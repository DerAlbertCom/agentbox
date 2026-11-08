namespace Abox;

/// <summary>
/// An error that abox reports to the user and that ends the command with exit code 1.
/// </summary>
public sealed class AboxException : Exception
{
    public AboxException(string message)
        : base(message)
    {
    }

    public AboxException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
