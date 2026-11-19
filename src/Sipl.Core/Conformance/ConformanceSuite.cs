using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Sipl.Runtime;
using Sipl.Syntax;

namespace Sipl.Conformance;

public sealed record ConformanceCase(string Name, string SourcePath, string Description,
    Dictionary<string, BigInteger> Input, int MaxSteps, JsonObject Expect)
{
    public static ConformanceCase Load(string root, string jsonPath)
    {
        var sipl = Path.ChangeExtension(jsonPath, ".sipl");
        if (!File.Exists(sipl))
        {
            throw new FormatException($"{jsonPath}: немає файлу {Path.GetFileName(sipl)}");
        }

        JsonObject spec;
        try
        {
            spec = JsonNode.Parse(File.ReadAllText(jsonPath))!.AsObject();
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException)
        {
            throw new FormatException($"{jsonPath}: некоректний JSON");
        }

        if (spec["expect"]?["status"]?.ToString() is not ("ok" or "syntax_error" or "runtime_error" or "step_limit"))
        {
            throw new FormatException($"{jsonPath}: некоректне поле expect.status");
        }

        var input = (spec["input"]?.AsObject() ?? [])
            .ToDictionary(kv => kv.Key, kv => BigInteger.Parse(kv.Value!.ToJsonString(), CultureInfo.InvariantCulture));
        var name = Path.GetRelativePath(root, jsonPath).Replace('\\', '/')[..^".json".Length];
        return new ConformanceCase(name, sipl, (string?)spec["description"] ?? "", input,
            (int?)spec["max_steps"] ?? Interpreter.DefaultMaxSteps, spec["expect"]!.AsObject());
    }

    public static List<ConformanceCase> Discover(string root, string? filter = null) =>
        Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(p => Load(root, p))
            .Where(c => filter is null || c.Name.Contains(filter))
            .ToList();
}

public sealed record Observation(string Status, JsonObject? State, JsonObject? Error);

public interface ISiplImplementation
{
    Observation Run(ConformanceCase c);

    JsonNode? ParseAst(ConformanceCase c);
}

public sealed class BuiltinImplementation : ISiplImplementation
{
    public Observation Run(ConformanceCase c)
    {
        var res = Parser.ParseSource(File.ReadAllText(c.SourcePath));
        if (!res.Ok)
        {
            return new Observation("syntax_error", null, Error(res.Diagnostics[0]));
        }

        var r = Interpreter.Run(res.Program!, c.Input, c.MaxSteps);
        return new Observation(r.StatusName, Json.State(r.State), r.Diagnostic is null ? null : Error(r.Diagnostic));
    }

    public JsonNode? ParseAst(ConformanceCase c) =>
        Parser.ParseSource(File.ReadAllText(c.SourcePath)).Program?.ToJson(withSpans: false);

    private static JsonObject Error(Diagnostics.Diagnostic d) =>
        new() { ["code"] = d.Code, ["line"] = d.Span?.Line, ["column"] = d.Span?.Column };
}

public sealed class ProcessImplementation(string command) : ISiplImplementation
{
    private readonly List<string> _argv = SplitCommand(command);

    public static List<string> SplitCommand(string command)
    {
        var args = new List<string>();
        var cur = new StringBuilder();
        char? quote = null;
        var started = false;
        foreach (var ch in command)
        {
            if (quote is not null)
            {
                if (ch == quote)
                {
                    quote = null;
                }
                else
                {
                    cur.Append(ch);
                }
            }
            else if (ch is '"' or '\'')
            {
                quote = ch;
                started = true;
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (started)
                {
                    args.Add(cur.ToString());
                    cur.Clear();
                    started = false;
                }
            }
            else
            {
                cur.Append(ch);
                started = true;
            }
        }

        if (started)
        {
            args.Add(cur.ToString());
        }

        return args.Count > 0 ? args : throw new ArgumentException("порожня команда");
    }

    private (int Code, JsonObject Output) Call(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(_argv[0])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in _argv.Skip(1).Concat(args))
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(30_000))
        {
            p.Kill(true);
            throw new TimeoutException("реалізація не відповіла за 30 с");
        }

        try
        {
            return (p.ExitCode, JsonNode.Parse(stdout.Result)!.AsObject());
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or NullReferenceException)
        {
            throw new InvalidDataException($"реалізація повернула не JSON (код {p.ExitCode})");
        }
    }

    public Observation Run(ConformanceCase c)
    {
        var args = new List<string> { "run", c.SourcePath, "--json", "--max-steps", c.MaxSteps.ToString(CultureInfo.InvariantCulture) };
        if (c.Input.Count > 0)
        {
            args.Add("--set");
            args.AddRange(c.Input.Select(kv => $"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}"));
        }

        var (code, output) = Call(args);
        var status = (string?)output["result"]?["status"] ?? code switch
        {
            0 => "ok",
            1 => "syntax_error",
            3 => "runtime_error",
            4 => "step_limit",
            _ => $"exit_{code}",
        };
        var d = (output["diagnostics"] as JsonArray)?.FirstOrDefault();
        var error = d is null ? null : new JsonObject
        {
            ["code"] = (string?)d["code"],
            ["line"] = (int?)d["span"]?["line"],
            ["column"] = (int?)d["span"]?["column"],
        };
        return new Observation(status, output["result"]?["state"]?.DeepClone() as JsonObject, error);
    }

    public JsonNode? ParseAst(ConformanceCase c) =>
        Call(["parse", c.SourcePath, "--json", "--no-spans"]).Output["result"]?["ast"]?.DeepClone();
}

public sealed record CaseResult(ConformanceCase Case, List<string> Failures)
{
    public bool Passed => Failures.Count == 0;
}

public static class ConformanceRunner
{
    private static string Show(JsonNode? n) => n?.ToJsonString() ?? "null";

    public static CaseResult Check(ConformanceCase c, ISiplImplementation impl)
    {
        var failures = new List<string>();
        var exp = c.Expect;
        try
        {
            var obs = impl.Run(c);
            if (obs.Status != (string)exp["status"]!)
            {
                failures.Add($"status: очікувалось {exp["status"]}, отримано {obs.Status}");
            }

            if (exp["state"] is JsonObject state && !Json.DeepEquals(state, obs.State))
            {
                failures.Add($"state: очікувалось {Show(state)}, отримано {Show(obs.State)}");
            }

            if (exp["state_subset"] is JsonObject subset && subset.Any(kv => !Json.DeepEquals(kv.Value, obs.State?[kv.Key])))
            {
                failures.Add($"state_subset: очікувалось {Show(subset)}, отримано {Show(obs.State)}");
            }

            if (exp["error"] is JsonObject want)
            {
                foreach (var (key, value) in want)
                {
                    if (!Json.DeepEquals(value, obs.Error?[key]))
                    {
                        failures.Add($"error.{key}: очікувалось {Show(value)}, отримано {Show(obs.Error?[key])}");
                    }
                }
            }

            if (exp["ast"] is JsonObject ast && !Json.DeepEquals(ast, impl.ParseAst(c)))
            {
                failures.Add("ast: дерево не збігається");
            }
        }
        catch (Exception e)
        {
            failures.Add($"реалізація не відпрацювала: {e.Message}");
        }

        return new CaseResult(c, failures);
    }
}
