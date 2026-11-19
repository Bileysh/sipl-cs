using System.Numerics;
using System.Text.Json.Nodes;
using Sipl.Text;

namespace Sipl.Ast;

public abstract class Node
{
    public SourceSpan Span { get; internal set; }

    public int Id { get; internal set; } = -1;

    public string Kind => GetType().Name;

    public abstract IEnumerable<Node> Children { get; }

    public IEnumerable<Node> Walk() => Children.SelectMany(c => c.Walk()).Prepend(this);

    protected abstract IEnumerable<(string Key, object Value)> Fields();

    public JsonObject ToJson(bool withSpans = true)
    {
        var obj = new JsonObject { ["kind"] = Kind };
        if (withSpans)
        {
            obj["id"] = Id;
            obj["span"] = Span.ToJson();
        }

        foreach (var (key, value) in Fields())
        {
            obj[key] = value switch
            {
                Node n => n.ToJson(withSpans),
                IEnumerable<Node> list => new JsonArray(list.Select(n => (JsonNode)n.ToJson(withSpans)).ToArray()),
                BigInteger b => Json.Number(b),
                bool b => b,
                string s => s,
                _ => throw new InvalidOperationException(),
            };
        }

        return obj;
    }
}

public abstract class Stmt : Node;

public abstract class AExpr : Node;

public abstract class BExpr : Node;

public sealed class Num(BigInteger value) : AExpr
{
    public BigInteger Value { get; } = value;
    public override IEnumerable<Node> Children => [];
    protected override IEnumerable<(string, object)> Fields() => [("value", Value)];
}

public sealed class Var(string name) : AExpr
{
    public string Name { get; } = name;
    public override IEnumerable<Node> Children => [];
    protected override IEnumerable<(string, object)> Fields() => [("name", Name)];
}

public sealed class Neg(AExpr operand) : AExpr
{
    public AExpr Operand { get; } = operand;
    public override IEnumerable<Node> Children => [Operand];
    protected override IEnumerable<(string, object)> Fields() => [("operand", Operand)];
}

public sealed class BinOp(string op, AExpr left, AExpr right) : AExpr
{
    public string Op { get; } = op;
    public AExpr Left { get; } = left;
    public AExpr Right { get; } = right;
    public override IEnumerable<Node> Children => [Left, Right];
    protected override IEnumerable<(string, object)> Fields() => [("op", Op), ("left", Left), ("right", Right)];
}

public sealed class BoolConst(bool value) : BExpr
{
    public bool Value { get; } = value;
    public override IEnumerable<Node> Children => [];
    protected override IEnumerable<(string, object)> Fields() => [("value", Value)];
}

public sealed class Compare(string op, AExpr left, AExpr right) : BExpr
{
    public string Op { get; } = op;
    public AExpr Left { get; } = left;
    public AExpr Right { get; } = right;
    public override IEnumerable<Node> Children => [Left, Right];
    protected override IEnumerable<(string, object)> Fields() => [("op", Op), ("left", Left), ("right", Right)];
}

public sealed class Not(BExpr operand) : BExpr
{
    public BExpr Operand { get; } = operand;
    public override IEnumerable<Node> Children => [Operand];
    protected override IEnumerable<(string, object)> Fields() => [("operand", Operand)];
}

public sealed class BoolOp(string op, BExpr left, BExpr right) : BExpr
{
    public string Op { get; } = op;
    public BExpr Left { get; } = left;
    public BExpr Right { get; } = right;
    public override IEnumerable<Node> Children => [Left, Right];
    protected override IEnumerable<(string, object)> Fields() => [("op", Op), ("left", Left), ("right", Right)];
}

public sealed class Assign(string target, AExpr value) : Stmt
{
    public string Target { get; } = target;
    public AExpr Value { get; } = value;
    public override IEnumerable<Node> Children => [Value];
    protected override IEnumerable<(string, object)> Fields() => [("target", Target), ("value", Value)];
}

public sealed class Seq(IReadOnlyList<Stmt> body) : Stmt
{
    public IReadOnlyList<Stmt> Body { get; } = body;
    public override IEnumerable<Node> Children => Body;
    protected override IEnumerable<(string, object)> Fields() => [("body", Body)];
}

public sealed class If(BExpr cond, Stmt then, Stmt orElse) : Stmt
{
    public BExpr Cond { get; } = cond;
    public Stmt Then { get; } = then;
    public Stmt OrElse { get; } = orElse;
    public override IEnumerable<Node> Children => [Cond, Then, OrElse];
    protected override IEnumerable<(string, object)> Fields() => [("cond", Cond), ("then", Then), ("orelse", OrElse)];
}

public sealed class While(BExpr cond, Stmt body) : Stmt
{
    public BExpr Cond { get; } = cond;
    public Stmt Body { get; } = body;
    public override IEnumerable<Node> Children => [Cond, Body];
    protected override IEnumerable<(string, object)> Fields() => [("cond", Cond), ("body", Body)];
}

public sealed class Block(Stmt body) : Stmt
{
    public Stmt Body { get; } = body;
    public override IEnumerable<Node> Children => [Body];
    protected override IEnumerable<(string, object)> Fields() => [("body", Body)];
}

public sealed class Skip : Stmt
{
    public override IEnumerable<Node> Children => [];
    protected override IEnumerable<(string, object)> Fields() => [];
}

public sealed class Program(Stmt body) : Node
{
    public Stmt Body { get; } = body;
    public override IEnumerable<Node> Children => [Body];
    protected override IEnumerable<(string, object)> Fields() => [("body", Body)];
}
