using System.Numerics;
using System.Text.Json.Nodes;
using Sipl.Ast;
using Sipl.Diagnostics;

namespace Sipl.Runtime;

public enum RunStatus
{
    Ok,
    RuntimeError,
    StepLimit,
}

public sealed record RunResult(RunStatus Status, IReadOnlyDictionary<string, BigInteger> State, int Steps, Diagnostic? Diagnostic)
{
    public bool Ok => Status == RunStatus.Ok;

    public string StatusName => Status switch
    {
        RunStatus.Ok => "ok",
        RunStatus.RuntimeError => "runtime_error",
        _ => "step_limit",
    };

    public JsonObject ToJson() => new()
    {
        ["status"] = StatusName,
        ["state"] = Json.State(State),
        ["steps"] = Steps,
        ["diagnostic"] = Diagnostic?.ToJson(),
    };
}

public sealed record Trace(IReadOnlyList<ExecutionEvent> Events, RunResult Result)
{
    public JsonObject ToJson() => new()
    {
        ["schema"] = "sipl.trace/1",
        ["events"] = new JsonArray(Events.Select(e => (JsonNode)e.ToJson()).ToArray()),
        ["result"] = Result.ToJson(),
    };
}

public sealed class Interpreter
{
    public const int DefaultMaxSteps = 100_000;

    private readonly Ast.Program _program;
    private readonly Dictionary<string, BigInteger> _state;
    private readonly int _maxSteps;
    private readonly List<While> _loops = [];
    private int _depth = -1;
    private int _seq;
    private int _steps;

    public Interpreter(Ast.Program program, IReadOnlyDictionary<string, BigInteger>? input = null, int maxSteps = DefaultMaxSteps)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSteps, 1);
        _program = program;
        _state = input is null ? [] : new Dictionary<string, BigInteger>(input);
        _maxSteps = maxSteps;
    }

    public IReadOnlyDictionary<string, BigInteger> State => _state;

    public RunResult? Result { get; private set; }

    public static RunResult Run(Ast.Program program, IReadOnlyDictionary<string, BigInteger>? input = null,
        int maxSteps = DefaultMaxSteps) => new Interpreter(program, input, maxSteps).Run();

    public static Trace Trace(Ast.Program program, IReadOnlyDictionary<string, BigInteger>? input = null,
        int maxSteps = DefaultMaxSteps) => new Interpreter(program, input, maxSteps).RunTrace();

    public RunResult Run()
    {
        foreach (var _ in Events())
        {
        }

        return Result!;
    }

    public Trace RunTrace() => new(Events().ToList(), Result!);

    private T Emit<T>(T ev)
        where T : ExecutionEvent
    {
        ev.Seq = _seq++;
        ev.Depth = Math.Max(0, _depth);
        return ev;
    }

    public IEnumerable<ExecutionEvent> Events()
    {
        yield return Emit(new ProgramStarted(new Dictionary<string, BigInteger>(_state)));

        var status = RunStatus.Ok;
        Diagnostic? error = null;
        using (var inner = Exec(_program.Body).GetEnumerator())
        {
            while (true)
            {
                try
                {
                    if (!inner.MoveNext())
                    {
                        break;
                    }
                }
                catch (StepLimitExceededException e)
                {
                    (status, error) = (RunStatus.StepLimit, e.Diagnostic);
                    break;
                }
                catch (SiplRuntimeException e)
                {
                    (status, error) = (RunStatus.RuntimeError, e.Diagnostic);
                    break;
                }

                yield return inner.Current;
            }
        }

        _depth = -1;
        Result = new RunResult(status, new Dictionary<string, BigInteger>(_state), _steps, error);
        yield return Emit(new ProgramFinished(Result));
    }

    private void Tick(Node node)
    {
        if (++_steps > _maxSteps)
        {
            var where = _loops.Count > 0 ? _loops[^1] : node;
            throw new StepLimitExceededException(new Diagnostic(Codes.StepLimit,
                $"перевищено ліміт кроків ({_maxSteps}); мабуть, цикл не завершується", where.Span,
                "збільшити ліміт можна опцією --max-steps"));
        }
    }

    private IEnumerable<ExecutionEvent> Exec(Stmt node)
    {
        Tick(node);
        _depth++;
        var step = _steps;
        yield return Emit(new BeforeStatement(node, step));

        switch (node)
        {
            case Assign a:
                var value = Evaluator.Arith(a.Value, _state);
                BigInteger? old = _state.TryGetValue(a.Target, out var o) ? o : null;
                _state[a.Target] = value;
                yield return Emit(new VariableChanged(a, old, value));
                break;

            case Seq s:
                foreach (var ev in s.Body.SelectMany(Exec))
                {
                    yield return ev;
                }

                break;

            case Block b:
                foreach (var ev in Exec(b.Body))
                {
                    yield return ev;
                }

                break;

            case If i:
                var c = Evaluator.Bool(i.Cond, _state);
                yield return Emit(new ConditionEvaluated(i, i.Cond, c));
                foreach (var ev in Exec(c ? i.Then : i.OrElse))
                {
                    yield return ev;
                }

                break;

            case While w:
                _loops.Add(w);
                while (true)
                {
                    var cv = Evaluator.Bool(w.Cond, _state);
                    yield return Emit(new ConditionEvaluated(w, w.Cond, cv));
                    if (!cv)
                    {
                        break;
                    }

                    foreach (var ev in Exec(w.Body))
                    {
                        yield return ev;
                    }

                    Tick(w);
                }

                _loops.RemoveAt(_loops.Count - 1);
                break;
        }

        yield return Emit(new AfterStatement(node, step));
        _depth--;
    }
}
