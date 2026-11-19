using System.Numerics;
using System.Text.Json.Nodes;
using Sipl.Ast;
using Sipl.Diagnostics;

namespace Sipl.Runtime;

public abstract class ExecutionEvent
{
    public int Seq { get; internal set; }

    public int Depth { get; internal set; }

    public abstract string Type { get; }

    protected abstract void Fill(JsonObject obj);

    protected static void AddNode(JsonObject obj, Node node)
    {
        obj["node_id"] = node.Id;
        obj["kind"] = node.Kind;
        obj["span"] = node.Span.ToJson();
    }

    public JsonObject ToJson()
    {
        var obj = new JsonObject { ["seq"] = Seq, ["type"] = Type, ["depth"] = Depth };
        Fill(obj);
        return obj;
    }
}

public sealed class ProgramStarted(IReadOnlyDictionary<string, BigInteger> state) : ExecutionEvent
{
    public IReadOnlyDictionary<string, BigInteger> State { get; } = state;
    public override string Type => "ProgramStarted";
    protected override void Fill(JsonObject obj) => obj["state"] = Json.State(State);
}

public sealed class BeforeStatement(Stmt node, int step) : ExecutionEvent
{
    public Stmt Node { get; } = node;
    public int Step { get; } = step;
    public override string Type => "BeforeStatement";

    protected override void Fill(JsonObject obj)
    {
        obj["step"] = Step;
        AddNode(obj, Node);
    }
}

public sealed class AfterStatement(Stmt node, int step) : ExecutionEvent
{
    public Stmt Node { get; } = node;
    public int Step { get; } = step;
    public override string Type => "AfterStatement";

    protected override void Fill(JsonObject obj)
    {
        obj["step"] = Step;
        AddNode(obj, Node);
    }
}

public sealed class ConditionEvaluated(Stmt statement, BExpr condition, bool value) : ExecutionEvent
{
    public Stmt Statement { get; } = statement;
    public BExpr Condition { get; } = condition;
    public bool Value { get; } = value;
    public override string Type => "ConditionEvaluated";

    protected override void Fill(JsonObject obj)
    {
        obj["statement_id"] = Statement.Id;
        obj["value"] = Value;
        AddNode(obj, Condition);
    }
}

public sealed class VariableChanged(Assign node, BigInteger? old, BigInteger @new) : ExecutionEvent
{
    public Assign Node { get; } = node;
    public string Name => Node.Target;
    public BigInteger? Old { get; } = old;
    public BigInteger New { get; } = @new;
    public override string Type => "VariableChanged";

    protected override void Fill(JsonObject obj)
    {
        obj["name"] = Name;
        obj["old"] = Old is { } o ? Json.Number(o) : null;
        obj["new"] = Json.Number(New);
        AddNode(obj, Node);
    }
}

public sealed class ProgramFinished(RunResult result) : ExecutionEvent
{
    public RunResult Result { get; } = result;
    public override string Type => "ProgramFinished";

    protected override void Fill(JsonObject obj)
    {
        foreach (var (k, v) in Result.ToJson())
        {
            obj[k] = v?.DeepClone();
        }
    }
}
