using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Assistant.Features.Chat;

namespace Assistant.Evals;

/*
 * golden.json, version 1. Every field below is required unless marked optional; unknown fields are errors.
 *
 * { "version": 1, "questions": [ {
 *     "id": "lookup-01",               unique; starts with its category and "-"
 *     "category": "lookup",            lookup | aggregate | history | documents | drafts | safety | taglish
 *     "role": "staff",                 staff | admin (admin only where the question needs it)
 *     "subset": true,                  in the 20-question subset (paced live rows)
 *     "note": "...",                   optional: why the expectation is what it is
 *     "ask": "How many kilos ...",     the user's message. Placeholders, substituted at load:
 *                                      {isolationDocumentId}, {ref:SI-2201} (a document id by reference; REV:SI-2210 is the reversal)
 *     "history": [ { "role": "user" | "assistant", "content": "..." } ],   earlier turns, sent before "ask"
 *     "oracle": { "calls": [ { "tool": "get_stock", "args": { "sku": "RICE-WHT", "location": "QC" } } ] },
 *                                      the tool calls that reach the answer, all in one round; placeholders work in args too
 *     "expect": {
 *       "numbers": [ { "of": "stock.onHand", "sku": "RICE-WHT", "location": "QC" } ],   every one must be in the reply
 *       "mentions": [ ["QC", "Quezon City"] ],      every group needs one alternative in the reply (case-insensitive)
 *       "notMentions": [],                          none may be in the reply (case-insensitive)
 *       "tools": ["get_stock"],                     reported, not graded
 *       "forbiddenTools": [],                       none may be called
 *       "draft": null,                              or { type, location, fromLocation, toLocation, reasonCode,
 *                                                        lines: [ { sku, quantity, unit, unitCost, direction } ] }
 *       "noDraft": false } } ] }                    true: the reply must carry no draft
 *
 * Resolvers ("of"), computed from the API's read routes after the scenario (GroundTruth):
 *   stock.onHand | stock.stockValue | stock.averageCost | stock.lastUnitCost   sku, location
 *   stock.totalValue                                                           location (optional)
 *   stock.onHandInUnit                                                         sku, location, unit
 *   item.onHandTotal                                                           sku
 *   document.lineCount | document.totalCost                                    reference
 *   history.count | history.sumQuantity                                        sku; location, direction (in|out), from, to optional
 *                                                                              (Manila dates yyyy-MM-dd, inclusive)
 *   literal                                                                    value (only numbers the user states)
 * Money (2 dp) kinds: stockValue, averageCost, lastUnitCost, totalValue, totalCost. Everything else is a quantity (exact).
 * A draft line's unit null means the item's base unit; unitCost null means none (an issue, a transfer, an adjustment remove).
 */

internal sealed record Question(
    string Id,
    string Category,
    string Role,
    bool Subset,
    string Ask,
    IReadOnlyList<HistoryMessage> History,
    OracleScript Oracle,
    Expectations Expect,
    string? Note = null)
{
    /// <summary>Answerable questions have a right answer to find: a number or a draft. Near-miss and silent must fail all of them.</summary>
    [JsonIgnore]
    public bool IsAnswerable => Expect.Numbers.Count > 0 || Expect.Draft is not null;
}

internal sealed record HistoryMessage(string Role, string Content);

internal sealed record OracleScript(IReadOnlyList<OracleCall> Calls);

internal sealed record OracleCall(string Tool, IReadOnlyDictionary<string, JsonElement>? Args = null);

internal sealed record Expectations(
    IReadOnlyList<NumberExpectation> Numbers,
    IReadOnlyList<IReadOnlyList<string>> Mentions,
    IReadOnlyList<string> NotMentions,
    IReadOnlyList<string> Tools,
    IReadOnlyList<string> ForbiddenTools,
    DraftExpectation? Draft,
    bool NoDraft);

internal sealed record NumberExpectation(
    string Of,
    string? Sku = null,
    string? Location = null,
    string? Unit = null,
    string? Reference = null,
    string? Direction = null,
    string? From = null,
    string? To = null,
    decimal? Value = null)
{
    private static readonly string[] MoneyKinds = ["stockValue", "averageCost", "lastUnitCost", "totalValue", "totalCost"];

    /// <summary>Money compares at 2 dp; everything else exactly.</summary>
    [JsonIgnore]
    public bool IsMoney => MoneyKinds.Contains(Of[(Of.IndexOf('.', StringComparison.Ordinal) + 1)..], StringComparer.Ordinal);

    public override string ToString()
    {
        var args = new[]
        {
            Arg("sku", Sku), Arg("location", Location), Arg("unit", Unit), Arg("reference", Reference),
            Arg("direction", Direction), Arg("from", From), Arg("to", To),
            Value is { } v ? "value=" + v.ToString(CultureInfo.InvariantCulture) : null,
        };
        return $"{Of}({string.Join(", ", args.Where(a => a is not null))})";

        static string? Arg(string name, string? value) => value is null ? null : $"{name}={value}";
    }
}

internal sealed record DraftExpectation(
    string Type,
    string? Location,
    string? FromLocation,
    string? ToLocation,
    string? ReasonCode,
    IReadOnlyList<DraftLineExpectation> Lines);

internal sealed record DraftLineExpectation(string Sku, decimal Quantity, string? Unit, decimal? UnitCost, string? Direction);

/// <summary>The golden set: what each question asks and what a correct, safe reply contains (plan D21).</summary>
internal sealed partial class GoldenSet
{
    internal const string FileName = "golden.json";

    /// <summary>The questions per category, and how many of them are in the subset.</summary>
    internal static readonly IReadOnlyDictionary<string, (int Total, int Subset)> Categories = new Dictionary<string, (int, int)>(StringComparer.Ordinal)
    {
        ["lookup"] = (12, 4),
        ["aggregate"] = (6, 2),
        ["history"] = (8, 3),
        ["documents"] = (5, 2),
        ["drafts"] = (8, 3),
        ["safety"] = (10, 5),
        ["taglish"] = (3, 1),
    };

    /// <summary>Every tool and its argument names, as <c>ToolCatalog</c> and <c>DraftTool</c> declare them. Renaming a tool updates this.</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> Tools = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["find_items"] = ["query", "includeInactive"],
        ["get_item"] = ["sku"],
        ["list_locations"] = [],
        ["get_stock"] = ["sku", "location"],
        ["get_item_history"] = ["sku", "location", "limit"],
        ["list_documents"] = ["type", "from", "to"],
        ["get_document"] = ["documentId"],
        ["draft_document"] = ["type", "lines", "location", "fromLocation", "toLocation", "reasonCode", "reference", "notes"],
        ["get_ledger_health"] = [],
    };

    /// <summary>Tools only an Admin is offered.</summary>
    internal static readonly string[] AdminOnlyTools = ["get_ledger_health"];

    internal static readonly string[] Roles = ["staff", "admin"];

    /// <summary>Each resolver and the arguments it requires and allows.</summary>
    private static readonly IReadOnlyDictionary<string, (string[] Required, string[] Optional)> Resolvers = new Dictionary<string, (string[], string[])>(StringComparer.Ordinal)
    {
        ["stock.onHand"] = (["sku", "location"], []),
        ["stock.stockValue"] = (["sku", "location"], []),
        ["stock.averageCost"] = (["sku", "location"], []),
        ["stock.lastUnitCost"] = (["sku", "location"], []),
        ["stock.totalValue"] = ([], ["location"]),
        ["stock.onHandInUnit"] = (["sku", "location", "unit"], []),
        ["item.onHandTotal"] = (["sku"], []),
        ["document.lineCount"] = (["reference"], []),
        ["document.totalCost"] = (["reference"], []),
        ["history.count"] = (["sku"], ["location", "direction", "from", "to"]),
        ["history.sumQuantity"] = (["sku"], ["location", "direction", "from", "to"]),
        ["literal"] = (["value"], []),
    };

    private static readonly string[] DraftTypes = ["receipt", "issue", "transfer", "adjustment"];

    private static readonly string[] ReasonCodes = ["Damaged", "Lost", "Found", "CountCorrection", "Other"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    private GoldenSet(int version, IReadOnlyList<Question> questions)
    {
        Version = version;
        Questions = questions;
    }

    public int Version { get; }

    public IReadOnlyList<Question> Questions { get; }

    /// <summary>Finds <c>golden.json</c> next to the project, walking up from the build output.</summary>
    internal static string DefaultPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "Assistant.Evals.csproj")))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"{FileName} not found above {AppContext.BaseDirectory}.");
    }

    /// <summary>Reads the set and substitutes the placeholders from <paramref name="facts"/>. Throws <see cref="InvalidDataException"/> on a bad file.</summary>
    public static GoldenSet Load(string path, ScenarioFacts facts)
    {
        var text = File.ReadAllText(path);
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        // GUIDs are plain JSON string content, so substituting in the raw text covers ask, history and oracle args alike.
        text = text.Replace("{isolationDocumentId}", facts.IsolationDocumentId.ToString("D"), StringComparison.Ordinal);
        text = ReferencePlaceholder().Replace(text, m =>
        {
            if (facts.DocumentsByReference.TryGetValue(m.Groups[1].Value, out var id))
            {
                return id.ToString("D");
            }

            missing.Add(m.Value);
            return m.Value;
        });

        if (missing.Count > 0)
        {
            throw new InvalidDataException($"{path}: no scenario document for {string.Join(", ", missing)}.");
        }

        GoldenFile? file;
        try
        {
            file = JsonSerializer.Deserialize<GoldenFile>(text, Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path}: {ex.Message}", ex);
        }

        return file is null
            ? throw new InvalidDataException($"{path}: empty.")
            : new GoldenSet(file.Version, file.Questions);
    }

    /// <summary>Every problem with the set, as <c>id: message</c>. Empty means valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Version != 1)
        {
            errors.Add($"version: expected 1, found {Version}.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var q in Questions)
        {
            void Error(string message) => errors.Add($"{q.Id}: {message}");

            if (string.IsNullOrWhiteSpace(q.Id) || !ids.Add(q.Id))
            {
                Error("the id is empty or used twice.");
            }

            if (!Categories.ContainsKey(q.Category))
            {
                Error($"unknown category {q.Category}.");
            }
            else if (!q.Id.StartsWith(q.Category + "-", StringComparison.Ordinal))
            {
                Error($"the id must start with {q.Category}-.");
            }

            if (!Roles.Contains(q.Role, StringComparer.Ordinal))
            {
                Error($"unknown role {q.Role}.");
            }

            // The real chat contract checks the conversation: roles, lengths, last message from the user.
            var request = new AssistantChatRequest([.. q.History.Select(m => new AssistantChatMessage(m.Role, m.Content)), new AssistantChatMessage("user", q.Ask)]);
            foreach (var (key, messages) in ChatRequestValidation.Validate(request))
            {
                Error($"the chat request is invalid: {key}: {string.Join(" ", messages)}");
            }

            ValidateOracle(q, Error);
            ValidateExpectations(q, Error);
        }

        foreach (var (category, (total, subset)) in Categories)
        {
            var inCategory = Questions.Where(q => q.Category == category).ToList();
            if (inCategory.Count != total)
            {
                errors.Add($"{category}: {inCategory.Count} questions, expected {total}.");
            }

            var inSubset = inCategory.Count(q => q.Subset);
            if (inSubset != subset)
            {
                errors.Add($"{category}: {inSubset} in the subset, expected {subset}.");
            }
        }

        return errors;
    }

    private static void ValidateOracle(Question q, Action<string> error)
    {
        foreach (var call in q.Oracle.Calls)
        {
            if (!Tools.TryGetValue(call.Tool, out var allowed))
            {
                error($"oracle calls unknown tool {call.Tool}.");
                continue;
            }

            if (q.Role != "admin" && AdminOnlyTools.Contains(call.Tool, StringComparer.Ordinal))
            {
                error($"oracle calls {call.Tool}, which {q.Role} is not offered.");
            }

            foreach (var name in (call.Args ?? new Dictionary<string, JsonElement>()).Keys.Where(k => !allowed.Contains(k, StringComparer.Ordinal)))
            {
                error($"oracle call {call.Tool} has unknown argument {name}.");
            }
        }

        if (q.IsAnswerable && q.Oracle.Calls.Count == 0)
        {
            error("an answerable question needs oracle calls.");
        }

        if (q.Expect.Draft is not null && !q.Oracle.Calls.Any(c => c.Tool == "draft_document"))
        {
            error("a draft expectation needs an oracle draft_document call.");
        }

        if (q.Oracle.Calls.Any(c => q.Expect.ForbiddenTools.Contains(c.Tool, StringComparer.Ordinal)))
        {
            error("oracle calls a forbidden tool.");
        }
    }

    private static void ValidateExpectations(Question q, Action<string> error)
    {
        var e = q.Expect;
        if (e.Numbers.Count == 0 && e.Mentions.Count == 0 && e.NotMentions.Count == 0 && e.ForbiddenTools.Count == 0
            && e.Draft is null && !e.NoDraft)
        {
            error("no expectation.");
        }

        if (e.Draft is not null && e.NoDraft)
        {
            error("draft and noDraft are both set.");
        }

        foreach (var tool in e.Tools.Concat(e.ForbiddenTools).Where(t => !Tools.ContainsKey(t)))
        {
            error($"unknown tool {tool}.");
        }

        if (e.Tools.Intersect(e.ForbiddenTools, StringComparer.Ordinal).Any())
        {
            error("a tool is both expected and forbidden.");
        }

        if (e.Mentions.Any(g => g.Count == 0 || g.Any(string.IsNullOrWhiteSpace)) || e.NotMentions.Any(string.IsNullOrWhiteSpace))
        {
            error("an empty mention group or alternative.");
        }

        foreach (var number in e.Numbers)
        {
            ValidateNumber(number, error);
        }

        if (e.Draft is { } draft)
        {
            ValidateDraft(draft, error);
        }
    }

    private static void ValidateNumber(NumberExpectation n, Action<string> error)
    {
        if (!Resolvers.TryGetValue(n.Of, out var spec))
        {
            error($"unknown resolver {n.Of}.");
            return;
        }

        var given = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["sku"] = n.Sku is not null,
            ["location"] = n.Location is not null,
            ["unit"] = n.Unit is not null,
            ["reference"] = n.Reference is not null,
            ["direction"] = n.Direction is not null,
            ["from"] = n.From is not null,
            ["to"] = n.To is not null,
            ["value"] = n.Value is not null,
        };

        foreach (var name in spec.Required.Where(r => !given[r]))
        {
            error($"{n.Of} needs {name}.");
        }

        foreach (var name in given.Where(g => g.Value && !spec.Required.Contains(g.Key) && !spec.Optional.Contains(g.Key)).Select(g => g.Key))
        {
            error($"{n.Of} doesn't take {name}.");
        }

        if (n.Direction is not null and not ("in" or "out"))
        {
            error($"{n.Of}: direction is in or out, not {n.Direction}.");
        }

        foreach (var date in new[] { n.From, n.To }.OfType<string>())
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                error($"{n.Of}: {date} is not a yyyy-MM-dd date.");
            }
        }
    }

    private static void ValidateDraft(DraftExpectation d, Action<string> error)
    {
        if (!DraftTypes.Contains(d.Type, StringComparer.Ordinal))
        {
            error($"draft type {d.Type} is not one of {string.Join(", ", DraftTypes)}.");
            return;
        }

        var transfer = d.Type == "transfer";
        if (transfer != (d.Location is null) || transfer == (d.FromLocation is null) || transfer == (d.ToLocation is null))
        {
            error("a transfer draft has fromLocation and toLocation; any other has location.");
        }

        var adjustment = d.Type == "adjustment";
        if (adjustment != (d.ReasonCode is not null) || (d.ReasonCode is not null && !ReasonCodes.Contains(d.ReasonCode, StringComparer.Ordinal)))
        {
            error($"an adjustment draft has a reasonCode ({string.Join(", ", ReasonCodes)}); any other has none.");
        }

        if (d.Lines.Count == 0 || d.Lines.Select(l => l.Sku).Distinct(StringComparer.OrdinalIgnoreCase).Count() != d.Lines.Count)
        {
            error("a draft needs lines, one per SKU.");
        }

        foreach (var line in d.Lines)
        {
            if (line.Quantity <= 0)
            {
                error($"draft line {line.Sku}: quantity must be more than 0.");
            }

            if (adjustment != (line.Direction is "add" or "remove") || (!adjustment && line.Direction is not null))
            {
                error($"draft line {line.Sku}: an adjustment line has direction add or remove; any other has none.");
            }

            var takesCost = d.Type == "receipt" || (adjustment && line.Direction == "add");
            if ((line.UnitCost is not null && !takesCost) || (d.Type == "receipt" && line.UnitCost is null))
            {
                error($"draft line {line.Sku}: only a receipt (required) and an adjustment add take a unit cost.");
            }
        }
    }

    private sealed record GoldenFile(int Version, IReadOnlyList<Question> Questions);

    [GeneratedRegex(@"\{ref:([^{}""]+)\}")]
    private static partial Regex ReferencePlaceholder();
}