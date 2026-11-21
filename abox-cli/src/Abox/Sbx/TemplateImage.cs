namespace Abox.Sbx;

/// <summary>A template image as <c>sbx template ls</c> reports it.</summary>
/// <param name="Repository">The registry path the image is stored in.</param>
/// <param name="Tag">The tag of the image.</param>
/// <param name="Id">The image id sbx reports.</param>
/// <param name="CreatedAt">When the image was built, when sbx reports it.</param>
public sealed record TemplateImage(
    string Repository,
    string Tag,
    string Id,
    DateTimeOffset? CreatedAt
)
{
    /// <summary>What sbx takes to name the image: repository and tag.</summary>
    public string Reference => $"{Repository}:{Tag}";
}
