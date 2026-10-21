using System.Text;
using AiAutomations.Core.Abstractions;

namespace AiAutomations.Infrastructure.Repositories;

/// <summary>
/// Prefers CLAUDE.md (several repos in this registry have one - written specifically to brief
/// an AI agent on the codebase, so it's the richest available context) over README.md, and
/// falls back to a shallow directory listing when neither exists.
/// </summary>
public class RepositoryInspector : IRepositoryInspector
{
    private const int MaxContextChars = 6000;
    private static readonly string[] PreferredDocs = ["CLAUDE.md", "README.md", "readme.md"];

    public async Task<string?> GetTechnicalContextAsync(string repoPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return null;
        }

        foreach (var docName in PreferredDocs)
        {
            var docPath = Path.Combine(repoPath, docName);
            if (File.Exists(docPath))
            {
                var text = await File.ReadAllTextAsync(docPath, cancellationToken);
                return $"From {docName}:\n{Truncate(text, MaxContextChars)}";
            }
        }

        return BuildStructureSummary(repoPath);
    }

    private static string BuildStructureSummary(string repoPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"No CLAUDE.md/README.md found - top-level contents of {repoPath}:");

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(repoPath).OrderBy(e => e).Take(40))
            {
                var name = Path.GetFileName(entry);
                sb.AppendLine(Directory.Exists(entry) ? $"- {name}/" : $"- {name}");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            sb.AppendLine("(could not list directory contents)");
        }

        return sb.ToString();
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "\n...(truncated)";
}
