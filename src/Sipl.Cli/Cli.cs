using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Sipl.Conformance;
using Sipl.Diagnostics;
using Sipl.Runtime;
using Sipl.Syntax;
using Sipl.Visualization;

namespace Sipl.Cli;

public static class ExitCodes
{
    public const int Ok = 0;
    public const int SyntaxError = 1;
    public const int Usage = 2;
    public const int RuntimeError = 3;
    public const int StepLimit = 4;
    public const int IoError = 5;

    public static int For(RunStatus status) => status switch
    {
        RunStatus.Ok => Ok,
        RunStatus.RuntimeError => RuntimeError,
        _ => StepLimit,
    };
}

internal sealed class UsageException(string message) : Exception(message);

public sealed class Cli(TextReader stdin, TextWriter stdout, TextWriter stderr)
{
    private const string Help = """
        Використання: sipl <команда> FILE [опції]     (FILE = '-' — читати зі stdin)

        Команди:
          parse        розібрати програму і показати AST
          check        перевірити синтаксис
          run          виконати програму
          trace        виконати і показати кожен крок
          tree         дерево виводу (-f text|json|dot|svg), AST (--ast), порівняння (--compare, -f text|html)
          conformance  [DIR] [--impl "КОМАНДА"] [-k ФІЛЬТР] — запустити conformance-тести

        Опції:
          --json             вивід у JSON (формат sipl.cli/1)
          --no-spans         parse --json: без позицій
          --set X=1 Y=2      значення вхідних змінних
          --max-steps N      ліміт кроків (за замовчуванням 100000)
          -f, --format F     формат для tree
          -o, --output FILE  записати результат tree у файл
          --version          версія

        Коди завершення: 0 — успіх, 1 — синтаксична помилка, 2 — неправильні аргументи,
        3 — помилка виконання, 4 — ліміт кроків, 5 — помилка читання/запису файлу.
        """;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        return new Cli(Console.In, Console.Out, Console.Error).Run(args);
    }

    private sealed class Options
    {
        public string Command = "";
        public string? File;
        public bool Json, NoSpans, Ast, Compare;
        public Dictionary<string, BigInteger> Input = [];
        public int MaxSteps = Interpreter.DefaultMaxSteps;
        public string Format = "text";
        public string? Output;
        public string? Impl;
        public string? Filter;
    }

    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        ["parse"] = ["--json", "--no-spans"],
        ["check"] = ["--json"],
        ["run"] = ["--json", "--set", "--max-steps"],
        ["trace"] = ["--json", "--set", "--max-steps"],
        ["tree"] = ["-f", "--format", "-o", "--output", "--ast", "--compare"],
        ["conformance"] = ["--impl", "-k"],
    };

    private static Options ParseArgs(IReadOnlyList<string> args)
    {
        var o = new Options { Command = args[0] };
        if (!Allowed.TryGetValue(o.Command, out var allowed))
        {
            throw new UsageException($"невідома команда '{o.Command}'");
        }

        string Value(ref int i) => i + 1 < args.Count && !args[i + 1].StartsWith("--")
            ? args[++i]
            : throw new UsageException($"опція {args[i]} потребує значення");

        for (var i = 1; i < args.Count; i++)
        {
            var a = args[i];
            if (a.StartsWith('-') && a != "-")
            {
                if (!allowed.Contains(a))
                {
                    throw new UsageException($"невідома опція '{a}' для команди {o.Command}");
                }

                switch (a)
                {
                    case "--json": o.Json = true; break;
                    case "--no-spans": o.NoSpans = true; break;
                    case "--ast": o.Ast = true; break;
                    case "--compare": o.Compare = true; break;
                    case "-o" or "--output": o.Output = Value(ref i); break;
                    case "--impl": o.Impl = Value(ref i); break;
                    case "-k": o.Filter = Value(ref i); break;
                    case "-f" or "--format":
                        o.Format = Value(ref i);
                        if (!TreeRenderer.Formats.Contains(o.Format) && o.Format != "html")
                        {
                            throw new UsageException($"невідомий формат '{o.Format}'");
                        }

                        break;
                    case "--max-steps":
                        var v = Value(ref i);
                        if (!int.TryParse(v, out o.MaxSteps) || o.MaxSteps < 1)
                        {
                            throw new UsageException($"--max-steps має бути додатним числом, а не '{v}'");
                        }

                        break;
                    case "--set":
                        var start = i;
                        while (i + 1 < args.Count && !args[i + 1].StartsWith("--"))
                        {
                            var (name, value) = ParseAssignment(args[++i]);
                            o.Input[name] = value;
                        }

                        if (i == start)
                        {
                            throw new UsageException("після --set має бути хоча б одне ІМ'Я=ЧИСЛО");
                        }

                        break;
                }
            }
            else if (o.File is null)
            {
                o.File = a;
            }
            else
            {
                throw new UsageException($"зайвий аргумент '{a}'");
            }
        }

        if (o.File is null && o.Command != "conformance")
        {
            throw new UsageException("не вказано файл програми");
        }

        return o;
    }

    private static (string, BigInteger) ParseAssignment(string item)
    {
        var parts = item.Split('=', 2);
        var name = parts[0].Trim();
        if (parts.Length != 2 || name.Length == 0 || !char.IsAsciiLetter(name[0])
            || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            throw new UsageException($"неправильне значення --set '{item}', треба ІМ'Я=ЧИСЛО");
        }

        return BigInteger.TryParse(parts[1].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)
            ? (name, v)
            : throw new UsageException($"значення {name} має бути цілим числом");
    }

    public int Run(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args[0] is "-h" or "--help")
        {
            (args.Count == 0 ? stderr : stdout).WriteLine(Help);
            return args.Count == 0 ? ExitCodes.Usage : ExitCodes.Ok;
        }

        if (args[0] == "--version")
        {
            stdout.WriteLine($"sipl {SiplInfo.Version}");
            return ExitCodes.Ok;
        }

        Options? o = null;
        try
        {
            o = ParseArgs(args);
            return o.Command switch
            {
                "parse" => Parse(o),
                "check" => Check(o),
                "run" => RunProgram(o),
                "trace" => Trace(o),
                "tree" => Tree(o),
                _ => Conformance(o),
            };
        }
        catch (UsageException e)
        {
            stderr.WriteLine($"sipl: помилка: {e.Message}");
            return ExitCodes.Usage;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (o?.Json == true)
            {
                Print(o, ExitCodes.IoError, [new Diagnostic("IO", e.Message, null)], null);
            }
            else
            {
                stderr.WriteLine($"sipl: не вдалося прочитати файл: {e.Message}");
            }

            return ExitCodes.IoError;
        }
    }

    private void Print(Options o, int code, IEnumerable<Diagnostic> diagnostics, JsonNode? result) =>
        stdout.WriteLine(Json.Serialize(new JsonObject
        {
            ["schema"] = "sipl.cli/1",
            ["version"] = SiplInfo.Version,
            ["command"] = o.Command,
            ["file"] = o.File,
            ["ok"] = code == ExitCodes.Ok,
            ["exit_code"] = code,
            ["diagnostics"] = new JsonArray(diagnostics.Select(d => (JsonNode)d.ToJson()).ToArray()),
            ["result"] = result,
        }));

    private (ParseResult? Result, string Source) Load(Options o)
    {
        var source = o.File == "-" ? stdin.ReadToEnd() : File.ReadAllText(o.File!);
        var result = Parser.ParseSource(source);
        if (result.Ok)
        {
            return (result, source);
        }

        if (o.Json)
        {
            Print(o, ExitCodes.SyntaxError, result.Diagnostics, null);
        }
        else
        {
            stderr.WriteLine(result.Diagnostics[0].Format(o.File!, source));
        }

        return (null, source);
    }

    private static string Show(IReadOnlyDictionary<string, BigInteger> state) =>
        string.Join("\n", state.Select(kv => $"  {kv.Key} = {kv.Value}"));

    private int Parse(Options o)
    {
        var (res, source) = Load(o);
        if (res is null)
        {
            return ExitCodes.SyntaxError;
        }

        if (o.Json)
        {
            Print(o, ExitCodes.Ok, [], new JsonObject { ["ast"] = res.Program!.ToJson(!o.NoSpans) });
        }
        else
        {
            stdout.WriteLine(TreeRenderer.ToText(VisNode.FromAst(res.Program!), source));
        }

        return ExitCodes.Ok;
    }

    private int Check(Options o)
    {
        var (res, _) = Load(o);
        if (res is null)
        {
            return ExitCodes.SyntaxError;
        }

        if (o.Json)
        {
            Print(o, ExitCodes.Ok, [], null);
        }
        else
        {
            stdout.WriteLine($"{o.File}: синтаксис правильний");
        }

        return ExitCodes.Ok;
    }

    private int RunProgram(Options o)
    {
        var (res, source) = Load(o);
        if (res is null)
        {
            return ExitCodes.SyntaxError;
        }

        var r = Interpreter.Run(res.Program!, o.Input, o.MaxSteps);
        var code = ExitCodes.For(r.Status);
        if (o.Json)
        {
            var result = r.ToJson();
            result.Remove("diagnostic");
            Print(o, code, r.Diagnostic is null ? [] : [r.Diagnostic], result);
        }
        else if (r.Ok)
        {
            stdout.WriteLine($"Готово, кроків: {r.Steps}. Кінцевий стан:\n{Show(r.State)}");
        }
        else
        {
            stderr.WriteLine(r.Diagnostic!.Format(o.File!, source));
            stderr.WriteLine($"Результат не визначений. Стан у момент зупинки:\n{Show(r.State)}");
        }

        return code;
    }

    private int Trace(Options o)
    {
        var (res, source) = Load(o);
        if (res is null)
        {
            return ExitCodes.SyntaxError;
        }

        var trace = Interpreter.Trace(res.Program!, o.Input, o.MaxSteps);
        var code = ExitCodes.For(trace.Result.Status);
        if (o.Json)
        {
            Print(o, code, trace.Result.Diagnostic is null ? [] : [trace.Result.Diagnostic], trace.ToJson());
            return code;
        }

        string Pad(ExecutionEvent e) => new(' ', 2 * e.Depth);
        foreach (var ev in trace.Events)
        {
            switch (ev)
            {
                case BeforeStatement { Node: Ast.Assign or Ast.Skip or Ast.If or Ast.While } b:
                    var text = string.Join(' ', b.Node.Span.Slice(source).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                    var head = text.Split(" then ")[0].Split(" do ")[0];
                    stdout.WriteLine($"{b.Node.Span.Line,4}: {Pad(ev)}{head}");
                    break;
                case VariableChanged v:
                    stdout.WriteLine($"      {Pad(ev)}  {v.Name}: {(v.Old?.ToString() ?? "⊥")} -> {v.New}");
                    break;
                case ConditionEvaluated c:
                    stdout.WriteLine($"      {Pad(ev)}  умова: {(c.Value ? "true" : "false")}");
                    break;
            }
        }

        if (trace.Result.Ok)
        {
            stdout.WriteLine($"Готово, кроків: {trace.Result.Steps}. Кінцевий стан:\n{Show(trace.Result.State)}");
        }
        else
        {
            stderr.WriteLine(trace.Result.Diagnostic!.Format(o.File!, source));
        }

        return code;
    }

    private int Tree(Options o)
    {
        var (res, source) = Load(o);
        if (res is null)
        {
            return ExitCodes.SyntaxError;
        }

        var name = Path.GetFileName(o.File!);
        string text;
        if (o.Compare)
        {
            text = o.Format switch
            {
                "text" => TreeComparison.Build(res).ToText(),
                "html" => TreeComparison.Build(res).ToHtml($"Дерево виводу і AST: {name}"),
                _ => throw new UsageException("--compare підтримує лише -f text або -f html"),
            };
        }
        else if (o.Format == "html")
        {
            throw new UsageException("формат html є лише для --compare");
        }
        else
        {
            var root = o.Ast ? VisNode.FromAst(res.Program!) : VisNode.FromParseTree(res.Tree!);
            text = TreeRenderer.Render(root, source, o.Format, o.Ast ? $"AST: {name}" : $"Дерево виводу: {name}");
        }

        if (o.Output is null)
        {
            stdout.Write(text);
        }
        else
        {
            File.WriteAllText(o.Output, text);
        }

        return ExitCodes.Ok;
    }

    private int Conformance(Options o)
    {
        var dir = o.File ?? Path.Combine("conformance", "cases");
        if (!Directory.Exists(dir))
        {
            throw new UsageException($"немає каталогу {dir}");
        }

        List<ConformanceCase> cases;
        try
        {
            cases = ConformanceCase.Discover(dir, o.Filter);
        }
        catch (FormatException e)
        {
            throw new UsageException(e.Message);
        }

        ISiplImplementation impl = o.Impl is null ? new BuiltinImplementation() : new ProcessImplementation(o.Impl);
        var passed = 0;
        foreach (var c in cases)
        {
            var r = ConformanceRunner.Check(c, impl);
            if (r.Passed)
            {
                passed++;
                continue;
            }

            stdout.WriteLine($"FAIL {c.Name} ({c.Description})");
            r.Failures.ForEach(f => stdout.WriteLine($"     {f}"));
        }

        stdout.WriteLine($"Пройдено {passed} з {cases.Count}");
        return cases.Count > 0 && passed == cases.Count ? ExitCodes.Ok : ExitCodes.SyntaxError;
    }
}
