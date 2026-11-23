using System.Globalization;

using Microsoft.Extensions.Configuration;

namespace Assistant.Evals;

/// <summary>
/// The evaluation harness. A console app on purpose: live rows take hours and need a model, so <c>dotnet test</c> and CI never run them.
/// No top-level statements, because the API's <c>Program</c> sits in the global namespace.
/// </summary>
internal static class EvalProgram
{
    private const string Usage = """
        Usage: dotnet run --project tests/Assistant.Evals -- <flags>
          --scenario-only     start the host, post the fixed scenario and print its fingerprint
          --validate-golden   run the scenario, then validate golden.json and resolve every expected number
          --check             calibrate the grader: oracle, near-miss and silent scripted rows through the real API (CI, T20)
          --rows <sel>        run live rows from matrix.json: local | hosted | all | name,name
          --subset            run only the 20 subset questions
          --out <dir>         results folder (default docs/evals/results/<yyyy-MM-dd>)
          --resume            skip questions that already have a non-error result in <out>
          --hardware "<text>" recorded in the matrix.md footer
        """;

    private static readonly string[] Modes = ["--scenario-only", "--validate-golden", "--check"];

    private static readonly string[] Switches = ["--subset", "--resume"];

    private static readonly string[] Valued = ["--rows", "--out", "--hardware"];

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(Usage);
            return 64;
        }

        // Valued flags take the next argument or --flag=value.
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var parts = args[i].Split('=', 2);
            if (Valued.Contains(parts[0]))
            {
                if (parts.Length == 2)
                {
                    values[parts[0]] = parts[1];
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    values[parts[0]] = args[++i];
                }
                else
                {
                    Console.Error.WriteLine($"{parts[0]} needs a value.");
                    return 64;
                }
            }
            else if (parts.Length == 1 && (Modes.Contains(parts[0]) || Switches.Contains(parts[0])))
            {
                flags.Add(parts[0]);
            }
            else
            {
                Console.Error.WriteLine($"Unknown flag: {args[i]}");
                Console.Error.WriteLine(Usage);
                return 64;
            }
        }

        var live = values.ContainsKey("--rows");
        if (live && Modes.Any(flags.Contains))
        {
            Console.Error.WriteLine("--rows cannot be combined with --scenario-only, --validate-golden or --check.");
            return 64;
        }

        if (!live && !Modes.Any(flags.Contains))
        {
            Console.Error.WriteLine("--subset, --resume, --out and --hardware need --rows.");
            return 64;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await using var host = new EvalHost();
        try
        {
            await host.StartAsync(cts.Token);
            var facts = await EvalScenario.RunAsync(host, cts.Token);
            if (live)
            {
                return await RunRowsAsync(host, facts, values, flags, cts.Token);
            }

            if (flags.Contains("--validate-golden"))
            {
                return await ValidateGoldenAsync(host, facts, cts.Token);
            }

            if (flags.Contains("--check"))
            {
                return await CheckAsync(host, facts, cts.Token);
            }

            Console.WriteLine($"fingerprint: {facts.Fingerprint}");
            Console.WriteLine($"now: {facts.Now:O}");
            foreach (var (reference, id) in facts.DocumentsByReference.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"{reference} -> {id}");
            }

            Console.WriteLine($"isolation CANARY-DOC -> {facts.IsolationDocumentId}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
    }

    /// <summary>
    /// Runs the selected <c>matrix.json</c> rows against real models, one fresh API per row. Each answer is appended to
    /// <c>&lt;out&gt;/&lt;row&gt;.jsonl</c> as it arrives. Keys come from user-secrets or env vars and are never written anywhere.
    /// </summary>
    private static async Task<int> RunRowsAsync(
        EvalHost host, ScenarioFacts facts, Dictionary<string, string> values, HashSet<string> flags, CancellationToken ct)
    {
        GoldenSet set;
        IReadOnlyList<MatrixRow> matrix;
        try
        {
            set = GoldenSet.Load(GoldenSet.DefaultPath(), facts);
            matrix = MatrixRow.Load();
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"rows: {ex.Message}");
            return 1;
        }

        var invalid = set.Validate();
        if (invalid.Count > 0)
        {
            foreach (var error in invalid)
            {
                Console.Error.WriteLine($"rows: golden error: {error}");
            }

            return 1;
        }

        var selector = values["--rows"];
        var selected = selector switch
        {
            "all" => matrix,
            "local" or "hosted" => [.. matrix.Where(r => r.Group == selector)],
            _ => [.. selector.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(n => matrix.FirstOrDefault(r => r.Name == n))
                .Where(r => r is not null).Select(r => r!)],
        };
        var unknown = selector is "all" or "local" or "hosted"
            ? []
            : selector.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(n => matrix.All(r => r.Name != n)).ToList();
        if (unknown.Count > 0 || selected.Count == 0)
        {
            Console.Error.WriteLine($"rows: no such row: {(unknown.Count > 0 ? string.Join(", ", unknown) : selector)}. Rows: {string.Join(", ", matrix.Select(r => r.Name))}");
            return 64;
        }

        var configuration = new ConfigurationBuilder().AddUserSecrets(typeof(EvalProgram).Assembly).AddEnvironmentVariables().Build();
        var outDir = Path.GetFullPath(values.GetValueOrDefault("--out") ?? Report.DefaultOutDirectory());
        Directory.CreateDirectory(outDir);
        var resume = flags.Contains("--resume");

        await using var truthApp = host.CreateApp(null);
        using var truthAdmin = await host.SignInAsync(truthApp, EvalScenario.Admin, ct);
        var runner = new EvalRunner(host, facts, new GroundTruth(truthAdmin, facts));

        var failed = false;
        foreach (var row in selected)
        {
            string? key = null;
            if (row.ApiKeySecret is { } secret)
            {
                key = configuration[secret];
                if (string.IsNullOrWhiteSpace(key))
                {
                    Console.WriteLine($"{row.Name}: skipped: set it with dotnet user-secrets set {secret} … --project tests/Assistant.Evals");
                    continue;
                }
            }

            var path = Report.CheckpointPath(outDir, row.Name);
            var done = resume ? Report.Answered(path) : [];
            var questions = set.Questions.Where(q => (!flags.Contains("--subset") && !row.SubsetOnly) || q.Subset)
                .Where(q => !done.Contains(q.Id)).ToList();
            if (!resume && File.Exists(path) && questions.Count > 0)
            {
                Console.Error.WriteLine($"{row.Name}: {path} already exists; rerun with --resume, or choose another --out.");
                failed = true;
                continue;
            }

            Console.WriteLine($"{row.Name}: {questions.Count} question(s) on {row.Model}{(done.Count > 0 ? $" ({done.Count} already answered)" : "")}");
            var settings = new List<(string Key, string Value)>
            {
                ("Assistant:Endpoint", row.Endpoint),
                ("Assistant:Model", row.Model),
                ("Assistant:TimeoutSeconds", row.TimeoutSeconds.ToString(CultureInfo.InvariantCulture)),
            };
            if (key is not null)
            {
                settings.Add(("Assistant:ApiKey", key));
            }

            await using var app = host.CreateApp(null, [.. settings]);
            var options = new RunOptions(new Throttle(row.RequestsPerMinute, row.TokensPerMinute, TimeProvider.System), row.TimeoutSeconds);
            var count = 0;
            var progress = new InlineProgress<QuestionResult>(r =>
            {
                Report.Append(path, r);
                count++;
                Console.WriteLine($"{row.Name}: [{count}/{questions.Count}] {r.Id} {(r.Passed ? "pass" : "FAIL")} {r.LatencyMs / 1000.0:0.0}s"
                    + (r.Passed ? "" : $" ({string.Join("; ", r.Reasons)})"));
            });

            await runner.RunAsync(app, questions, progress, ct, options);
            if (runner.DailyLimitRetryAfter is { } wait)
            {
                Console.WriteLine($"{row.Name}: daily limit reached; rerun with --resume after {DateTime.Now + wait:yyyy-MM-dd HH:mm}");
                failed = true;
            }
            else if (runner.Wrote.Count > 0)
            {
                Console.Error.WriteLine($"{row.Name}: the ledger changed during {string.Join(", ", runner.Wrote.Order(StringComparer.Ordinal))}");
            }
        }

        var matrixPath = Report.WriteMatrix(outDir, matrix, values.GetValueOrDefault("--hardware"));
        Console.WriteLine($"matrix: {matrixPath}");
        return failed ? 1 : 0;
    }

    /// <summary>Loads and validates the golden set, then resolves every expectation against the scenario. Exit 1 on any error.</summary>
    private static async Task<int> ValidateGoldenAsync(EvalHost host, ScenarioFacts facts, CancellationToken ct)
    {
        GoldenSet set;
        try
        {
            set = GoldenSet.Load(GoldenSet.DefaultPath(), facts);
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException)
        {
            Console.Error.WriteLine($"golden: {ex.Message}");
            return 1;
        }

        var errors = set.Validate().ToList();

        await using var app = host.CreateApp(null);
        using var admin = await host.SignInAsync(app, EvalScenario.Admin, ct);
        var truth = new GroundTruth(admin, facts);
        var resolved = 0;
        foreach (var q in set.Questions)
        {
            var values = new List<string>();
            foreach (var number in q.Expect.Numbers)
            {
                try
                {
                    values.Add($"{number} = {(await truth.ResolveAsync(number, ct)).ToString(CultureInfo.InvariantCulture)}");
                    resolved++;
                }
                catch (InvalidOperationException ex)
                {
                    errors.Add($"{q.Id}: {number}: {ex.Message}");
                }
            }

            if (q.Expect.Draft is { } draft)
            {
                try
                {
                    await truth.CheckDraftAsync(draft, ct);
                    values.Add($"draft {draft.Type} {string.Join(", ", draft.Lines.Select(l => $"{l.Sku} {l.Quantity.ToString(CultureInfo.InvariantCulture)} {l.Unit ?? "(base)"}"))}");
                }
                catch (InvalidOperationException ex)
                {
                    errors.Add($"{q.Id}: draft: {ex.Message}");
                }
            }

            Console.WriteLine($"golden: {q.Id} → {(values.Count == 0 ? "(no numbers)" : string.Join("; ", values))}");
        }

        var expectations = set.Questions.Sum(q => q.Expect.Numbers.Count);
        Console.WriteLine($"golden: {set.Questions.Count} questions, {set.Questions.Count(q => q.Subset)} in the subset, "
            + $"{set.Questions.Count(q => q.IsAnswerable)} answerable, {resolved}/{expectations} numbers resolved");
        foreach (var error in errors)
        {
            Console.Error.WriteLine($"golden error: {error}");
        }

        return errors.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// Calibrates the grader (T20, plan D17). Each scripted row runs the whole golden set through the real API and tools on its
    /// own factory. Exit 1 unless the oracle passes every question, near-miss and silent pass no answerable question, and no
    /// question changed the ledger.
    /// </summary>
    private static async Task<int> CheckAsync(EvalHost host, ScenarioFacts facts, CancellationToken ct)
    {
        GoldenSet set;
        try
        {
            set = GoldenSet.Load(GoldenSet.DefaultPath(), facts);
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException)
        {
            Console.Error.WriteLine($"check: golden: {ex.Message}");
            return 1;
        }

        var invalid = set.Validate();
        if (invalid.Count > 0)
        {
            foreach (var error in invalid)
            {
                Console.Error.WriteLine($"check: golden error: {error}");
            }

            return 1;
        }

        await using var truthApp = host.CreateApp(null);
        using var truthAdmin = await host.SignInAsync(truthApp, EvalScenario.Admin, ct);
        var runner = new EvalRunner(host, facts, new GroundTruth(truthAdmin, facts));

        IReadOnlyDictionary<string, IReadOnlyList<decimal>> expected;
        try
        {
            expected = await runner.ResolveExpectedAsync(set.Questions, ct);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"check: cannot resolve the expected numbers: {ex.Message}");
            return 1;
        }

        var answerable = set.Questions.Count(q => q.IsAnswerable);
        var violations = new List<string>();
        var table = new List<string> { $"{"row",-10} {"passed",-8} {"answerable failed",-18} writes" };
        foreach (var mode in new[] { ScriptedMode.Oracle, ScriptedMode.NearMiss, ScriptedMode.Silent })
        {
            await using var app = host.CreateApp(new GoldenScriptedClient(set, expected, mode));
            var results = await runner.RunAsync(app, set.Questions, null, ct);
            var row = mode.ToString().ToLowerInvariant();
            var passed = results.Count(r => r.Passed);
            var answerableFailed = results.Count(r => !r.Passed && r.IsAnswerable(set));
            table.Add($"{row,-10} {$"{passed}/{results.Count}",-8} {$"{answerableFailed}/{answerable}",-18} {runner.Wrote.Count}");

            foreach (var r in results)
            {
                var shouldPass = mode == ScriptedMode.Oracle;
                var isAnswerable = r.IsAnswerable(set);
                if (shouldPass && !r.Passed)
                {
                    violations.Add($"{row}: {r.Id} failed: {string.Join("; ", r.Reasons)}");
                }
                else if (!shouldPass && isAnswerable && r.Passed)
                {
                    violations.Add($"{row}: {r.Id} passed but must fail (reply: {r.Reply})");
                }
            }

            violations.AddRange(runner.Wrote.Order(StringComparer.Ordinal).Select(id => $"{row}: {id} changed the ledger"));
        }

        foreach (var line in table)
        {
            Console.WriteLine(line);
        }

        foreach (var violation in violations)
        {
            Console.Error.WriteLine($"check: {violation}");
        }

        Console.WriteLine(violations.Count == 0 ? "check: calibration holds." : $"check: calibration FAILED ({violations.Count} violation(s)).");
        return violations.Count == 0 ? 0 : 1;
    }

    private static bool IsAnswerable(this QuestionResult result, GoldenSet set) =>
        set.Questions.Single(q => q.Id == result.Id).IsAnswerable;
}