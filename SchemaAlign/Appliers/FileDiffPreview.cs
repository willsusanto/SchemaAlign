using SchemaAlign.Models.Diff;

namespace SchemaAlign.Appliers;

public class FileDiffPreview
{
    public string FilePath { get; set; } = string.Empty;
    public DiffType DiffType { get; set; } = DiffType.None;
    public string? OriginalContent { get; set; }
    public string? NewContent { get; set; }
    public string UnifiedDiff { get; set; } = string.Empty;
}
