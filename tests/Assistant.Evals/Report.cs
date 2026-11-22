using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Assistant.Evals;

/// <summary>One row of <c>matrix.json</c>. <see cref="ApiKeySecret"/> names a configuration key (user-secrets or an env var), never a value.</summary>
internal sealed record MatrixRow(
    string Name,
    string Group,
    string Endpoint,
    string Model,
    string? ApiKeySecret = null,
    int? RequestsPerMinute = null,
    int? TokensPerMinute = null,
    int TimeoutSeconds = 110,
    bool SubsetOnly = false)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal const string FileName = "matrix.json";

    public static IReadOnlyList<MatrixRow> Load()
    {
        var path = Report.FindUp(FileName);
        var file = JsonSerializer.Deserialize<MatrixFile>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException($"{FileName} is empty.");
        return file.Rows;
    }

    private sealed record MatrixFile(IReadOnlyList<MatrixRow> Rows);
}

/// <summary>An <see cref="IProgress{T}"/> that runs inline, so checkpoint lines are written in order and before the next question starts.</summary>
internal sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

/// <summary>The JSONL checkpoint (one <see cref="QuestionResult"/> per line) and the <c>matrix.md</c> table built from it.</summary>
internal static class Report
{
    private static readonly string[] Categories = ["lookup", "aggregate", "history", "documents", "drafts", "safety", "taglish"];

    /// <summary>Finds a file next to the project, walking up from the output folder like <see cref="GoldenSet.DefaultPath"/>.</summary>
    internal static string FindUp(string fileName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "Assistant.Evals.csproj")))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"{fileName} not found above {AppContext.BaseDirectory}.");
    }

    /// <summary>The repository root: the folder with the solution file.</summary>
    internal static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "InventorySystem.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("InventorySystem.slnx not found above " + AppContext.BaseDirectory);
    }

    public static string DefaultOutDirectory() =>
        Path.Combine(RepoRoot(), "docs", "evals", "results", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    public static string CheckpointPath(string outDir, string row) => Path.Combine(outDir, row + ".jsonl");

    /// <summary>Appends one result and flushes, so a stopped run loses nothing already answered.</summary>
    public static void Append(string path, QuestionResult result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.WriteLine(JsonSerializer.Serialize(result, MatrixRow.Json));
        writer.Flush();
        stream.Flush(true);
    }

    public static IReadOnlyList<QuestionResult> Load(string path) =>
        File.Exists(path)
            ? [.. File.ReadAllLines(path).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<QuestionResult>(l, MatrixRow.Json)!)]
            : [];

    /// <summary>A result is an error when the API did not answer 200 (a timeout is status 0). <c>--resume</c> runs those again.</summary>
    public static bool IsError(QuestionResult r) => r.Status != 200;

    /// <summary>Ids that already have a non-error result.</summary>
    public static HashSet<string> Answered(string path) =>
        Load(path).Where(r => !IsError(r)).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

    /// <summary>Reads every <c>*.jsonl</c> in <paramref name="outDir"/> and writes <c>matrix.md</c>. Returns its path.</summary>
    public static string WriteMatrix(string outDir, IReadOnlyList<MatrixRow> rows, string? hardware)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Assistant evaluation matrix").AppendLine();
        sb.AppendLine("| Row | Model | Group | Questions | **Accuracy** | Lookup | Aggregate | History | Documents | Drafts | Safety | Taglish | Tool use | p50 s | p95 s | avg in tok | avg out tok | Errors |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

        var subsetRows = new List<string>();
        foreach (var file in Directory.GetFiles(outDir, "*.jsonl").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var results = Load(file);
            if (results.Count == 0)
            {
                continue;
            }

            var row = rows.FirstOrDefault(r => r.Name == name);

            // Only the 20-question subset ran when every answer is a subset question.
            var onSubset = results.All(r => r.Subset);
            if (onSubset)
            {
                subsetRows.Add(name);
            }

            var passed = results.Count(r => r.Passed);
            var cells = Categories.Select(c =>
            {
                var inCategory = results.Where(r => r.Category == c).ToList();
                return inCategory.Count == 0 ? "–" : $"{inCategory.Count(r => r.Passed)}/{inCategory.Count}";
            });
            var latencies = results.Where(r => r.Status == 200).Select(r => r.LatencyMs / 1000.0).Order().ToList();
            var withIn = results.Where(r => r.InputTokens is not null).ToList();
            var withOut = results.Where(r => r.OutputTokens is not null).ToList();

            sb.Append("| ").Append(name).Append(onSubset ? " †" : "")
                .Append(" | ").Append(row?.Model ?? "?")
                .Append(" | ").Append(row?.Group ?? "?")
                .Append(" | ").Append(results.Count)
                .Append(" | **").Append(Percent(passed, results.Count)).Append("**")
                .Append(" | ").Append(string.Join(" | ", cells))
                .Append(" | ").Append(Percent(results.Count(r => r.ExpectedToolsUsed), results.Count))
                .Append(" | ").Append(Seconds(latencies, 0.50))
                .Append(" | ").Append(Seconds(latencies, 0.95))
                .Append(" | ").Append(withIn.Count == 0 ? "–" : Math.Round(withIn.Average(r => r.InputTokens!.Value)).ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(withOut.Count == 0 ? "–" : Math.Round(withOut.Average(r => r.OutputTokens!.Value)).ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(results.Count(IsError))
                .AppendLine(" |");
        }

        sb.AppendLine();
        if (subsetRows.Count > 0)
        {
            sb.AppendLine("† ran the 20-question subset only: its accuracy is not comparable with a full-set row.").AppendLine();
        }

        sb.AppendLine($"- Date: {DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"- Commit: {GitShortHead()}");
        sb.AppendLine($"- Hardware (local rows): {(string.IsNullOrWhiteSpace(hardware) ? "not given" : hardware)}");
        sb.AppendLine("- Temperature: 0");
        foreach (var row in rows.Where(r => r.RequestsPerMinute is not null || r.TokensPerMinute is not null))
        {
            sb.AppendLine($"- Limits, {row.Name}: {(row.RequestsPerMinute is { } r ? $"{r} requests/min" : "no request limit")}, "
                + $"{(row.TokensPerMinute is { } t ? $"{t} tokens/min" : "no token limit")}");
        }

        sb.AppendLine("- All rows $0; tokens stand in for cost.");

        var path = Path.Combine(outDir, "matrix.md");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    private static string Percent(int part, int total) =>
        total == 0 ? "–" : $"{part}/{total} ({(100.0 * part / total).ToString("0", CultureInfo.InvariantCulture)}%)";

    private static string Seconds(IReadOnlyList<double> sorted, double quantile)
    {
        if (sorted.Count == 0)
        {
            return "–";
        }

        // Nearest rank.
        var index = Math.Clamp((int)Math.Ceiling(quantile * sorted.Count) - 1, 0, sorted.Count - 1);
        return sorted[index].ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static string GitShortHead()
    {
        try
        {
            var info = new ProcessStartInfo("git", "rev-parse --short HEAD")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = RepoRoot(),
            };
            using var process = Process.Start(info);
            var text = process?.StandardOutput.ReadToEnd().Trim();
            process?.WaitForExit();
            return string.IsNullOrEmpty(text) ? "unknown" : text;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return "unknown";
        }
    }
}