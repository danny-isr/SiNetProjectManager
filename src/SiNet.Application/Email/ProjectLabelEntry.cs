namespace SiNet.Application.Email;

/// <summary>
/// One Gmail project leaf under the SiNet root. Identity is <see cref="ProjectNumber"/>, not path.
/// </summary>
public sealed record ProjectLabelEntry(
    string LabelId,
    string FullPath,
    int ProjectNumber,
    string ProjectDisplayName,
    string ParentPath);
