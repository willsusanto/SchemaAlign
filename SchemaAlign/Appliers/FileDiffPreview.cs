using SchemaAlign.Diff;

namespace SchemaAlign.Appliers;

public class FileDiffPreview
{
    public string FilePath { get; set; } = string.Empty;
    public DiffKind DiffKind { get; set; } = DiffKind.Unchanged;
    public string? OriginalContent { get; set; }
    public string? NewContent { get; set; }
    public string UnifiedDiff { get; set; } = string.Empty;
}
