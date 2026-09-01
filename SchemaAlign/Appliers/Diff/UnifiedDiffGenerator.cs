using System.Text;

namespace SchemaAlign.Appliers.Diff;

/// <summary>
/// Generates standard unified diff format strings comparing original and modified text.
/// </summary>
public static class UnifiedDiffGenerator
{
    /// <summary>
    /// Generates a standard unified diff string comparing originalText and modifiedText.
    /// </summary>
    /// <param name="originalText">The original file content, or null if adding a new file.</param>
    /// <param name="modifiedText">The modified file content, or null if deleting a file.</param>
    /// <param name="filePath">Relative or display file path for diff headers.</param>
    /// <returns>A unified diff string with hunks and line prefixes ('+', '-', ' ').</returns>
    public static string GenerateDiff(string? originalText, string? modifiedText, string filePath)
    {
        var sb = new StringBuilder();
        var normalizedPath = filePath.Replace('\\', '/');

        if (originalText == null && modifiedText == null)
        {
            return string.Empty;
        }

        if (originalText == null)
        {
            // Brand new file
            sb.AppendLine($"--- /dev/null");
            sb.AppendLine($"+++ b/{normalizedPath}");

            var modLines = SplitLines(modifiedText!);
            if (modLines.Length > 0)
            {
                sb.AppendLine($"@@ -0,0 +1,{modLines.Length} @@");
                foreach (var line in modLines)
                {
                    sb.AppendLine($"+{line}");
                }
            }
            return sb.ToString();
        }

        if (modifiedText == null)
        {
            // Deleted file
            sb.AppendLine($"--- a/{normalizedPath}");
            sb.AppendLine($"+++ /dev/null");

            var origLines = SplitLines(originalText);
            if (origLines.Length > 0)
            {
                sb.AppendLine($"@@ -1,{origLines.Length} +0,0 @@");
                foreach (var line in origLines)
                {
                    sb.AppendLine($"-{line}");
                }
            }
            return sb.ToString();
        }

        var oldLines = SplitLines(originalText);
        var newLines = SplitLines(modifiedText);

        var diffChunks = ComputeDiffChunks(oldLines, newLines);
        if (diffChunks.Count == 0)
        {
            return string.Empty;
        }

        sb.AppendLine($"--- a/{normalizedPath}");
        sb.AppendLine($"+++ b/{normalizedPath}");

        foreach (var chunk in diffChunks)
        {
            sb.AppendLine($"@@ -{chunk.OldStart},{chunk.OldCount} +{chunk.NewStart},{chunk.NewCount} @@");
            foreach (var line in chunk.Lines)
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString();
    }

    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (normalized.EndsWith('\n'))
        {
            normalized = normalized.Substring(0, normalized.Length - 1);
        }

        return normalized.Split('\n');
    }

    private class DiffChunk
    {
        public int OldStart { get; set; }
        public int OldCount { get; set; }
        public int NewStart { get; set; }
        public int NewCount { get; set; }
        public List<string> Lines { get; set; } = new();
    }

    private enum EditType { Equal, Insert, Delete }

    private record DiffEdit(EditType Type, string Line, int OldIndex, int NewIndex);

    private static List<DiffChunk> ComputeDiffChunks(string[] oldLines, string[] newLines, int contextLines = 3)
    {
        var edits = ComputeEdits(oldLines, newLines);

        // Group into hunks with context
        var hunks = new List<DiffChunk>();
        var i = 0;

        while (i < edits.Count)
        {
            if (edits[i].Type == EditType.Equal)
            {
                i++;
                continue;
            }

            // Found a change, determine chunk start and end with context
            var changeStart = i;
            var changeEnd = i;

            while (changeEnd < edits.Count)
            {
                if (edits[changeEnd].Type != EditType.Equal)
                {
                    changeEnd++;
                }
                else
                {
                    // Look ahead: if another change is within 2 * contextLines, combine
                    var nextChange = -1;
                    for (int k = changeEnd; k < Math.Min(edits.Count, changeEnd + 2 * contextLines + 1); k++)
                    {
                        if (edits[k].Type != EditType.Equal)
                        {
                            nextChange = k;
                            break;
                        }
                    }

                    if (nextChange != -1)
                    {
                        changeEnd = nextChange;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            var chunkStart = Math.Max(0, changeStart - contextLines);
            var chunkEnd = Math.Min(edits.Count - 1, changeEnd - 1 + contextLines);

            var chunk = new DiffChunk();
            var oldStart = 1;
            var newStart = 1;
            var oldIdxFound = false;
            var newIdxFound = false;

            var oldCount = 0;
            var newCount = 0;

            for (int j = chunkStart; j <= chunkEnd; j++)
            {
                var edit = edits[j];
                switch (edit.Type)
                {
                    case EditType.Equal:
                        if (!oldIdxFound && edit.OldIndex >= 0) { oldStart = edit.OldIndex + 1; oldIdxFound = true; }
                        if (!newIdxFound && edit.NewIndex >= 0) { newStart = edit.NewIndex + 1; newIdxFound = true; }
                        chunk.Lines.Add($" {edit.Line}");
                        oldCount++;
                        newCount++;
                        break;
                    case EditType.Delete:
                        if (!oldIdxFound && edit.OldIndex >= 0) { oldStart = edit.OldIndex + 1; oldIdxFound = true; }
                        chunk.Lines.Add($"-{edit.Line}");
                        oldCount++;
                        break;
                    case EditType.Insert:
                        if (!newIdxFound && edit.NewIndex >= 0) { newStart = edit.NewIndex + 1; newIdxFound = true; }
                        chunk.Lines.Add($"+{edit.Line}");
                        newCount++;
                        break;
                }
            }

            if (!oldIdxFound) oldStart = 1;
            if (!newIdxFound) newStart = 1;

            chunk.OldStart = oldStart;
            chunk.OldCount = oldCount;
            chunk.NewStart = newStart;
            chunk.NewCount = newCount;

            hunks.Add(chunk);
            i = chunkEnd + 1;
        }

        return hunks;
    }

    private static List<DiffEdit> ComputeEdits(string[] a, string[] b)
    {
        var n = a.Length;
        var m = b.Length;
        var lcs = new int[n + 1, m + 1];

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                if (a[i] == b[j])
                {
                    lcs[i + 1, j + 1] = lcs[i, j] + 1;
                }
                else
                {
                    lcs[i + 1, j + 1] = Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                }
            }
        }

        var edits = new List<DiffEdit>();
        int x = n, y = m;

        while (x > 0 || y > 0)
        {
            if (x > 0 && y > 0 && a[x - 1] == b[y - 1])
            {
                edits.Add(new DiffEdit(EditType.Equal, a[x - 1], x - 1, y - 1));
                x--;
                y--;
            }
            else if (y > 0 && (x == 0 || lcs[x, y - 1] >= lcs[x - 1, y]))
            {
                edits.Add(new DiffEdit(EditType.Insert, b[y - 1], -1, y - 1));
                y--;
            }
            else if (x > 0 && (y == 0 || lcs[x, y - 1] < lcs[x - 1, y]))
            {
                edits.Add(new DiffEdit(EditType.Delete, a[x - 1], x - 1, -1));
                x--;
            }
        }

        edits.Reverse();
        return edits;
    }
}
