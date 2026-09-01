namespace SchemaAlign.Appliers;

public class ApplierResult
{
    public bool Success { get; set; } = true;
    public List<string> ChangedFiles { get; set; } = new();
    public List<string> CreatedFiles { get; set; } = new();
    public List<string> DeletedFiles { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<FileDiffPreview> Previews { get; set; } = new();
}
