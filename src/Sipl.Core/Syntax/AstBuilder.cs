using System.Globalization;
using System.Numerics;
using Sipl.Ast;

namespace Sipl.Syntax;

internal sealed class AstBuilder(string source)
{
    private readonly Text.LineIndex _index = new(source);

    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["÷"] = "/",
        ["≤"] = "<=",
        ["≥"] = ">=",
        ["≠"] = "<>",
        ["!="] = "<>",
        ["∧"] = "and",
        ["&&"] = "and",
        ["∨"] = "or",
        ["||"] = "or",
    };

    private static string Op(string text) => Aliases.GetValueOrDefault(text, text);

    private T From<T>(T node, ParseNode origin)
        where T : Node
    {
        node.Span = origin.Span;
        return node;
    }

    public Ast.Program Build(ParseNode program)
    {
        var p = From(new Ast.Program(Seq(program.Children[0])), program);
        var id = 0;
        foreach (var n in p.Walk())
        {
            n.Id = id++;
        }

        return p;
    }

    private Stmt Seq(ParseNode seq)
    {
        var stmts = seq.Children.Where(c => !c.IsTerminal).Select(Statement).ToList();
        return stmts.Count == 1 ? stmts[0] : From(new Seq(stmts), seq);
    }

    private Stmt Statement(ParseNode statement)
    {
        var inner = statement.Children[0];
        var ch = inner.Children;
        Stmt result = inner.Symbol switch
        {
            "assign" => new Assign(ch[0].Text!, Expr(ch[2])),
            "if_stmt" => new If(Cond(ch[1]), Statement(ch[3]), Statement(ch[5])),
            "while_stmt" => new While(Cond(ch[1]), Statement(ch[3])),
            "block" => new Block(Seq(ch[1])),
            _ => new Skip(),
        };
        return From(result, inner);
    }

    private AExpr Expr(ParseNode node)
    {
        if (node.Symbol == "factor")
        {
            return Factor(node);
        }

        var ch = node.Children;
        var left = Expr(ch[0]);
        for (var i = 1; i < ch.Count; i += 2)
        {
            var right = Expr(ch[i + 1]);
            left = new BinOp(Op(ch[i].Text!), left, right) { Span = _index.Span(left.Span.Start, right.Span.End) };
        }

        return ch.Count > 1 ? From(left, node) : left;
    }

    private AExpr Factor(ParseNode node)
    {
        var ch = node.Children;
        if (ch.Count == 1)
        {
            return ch[0].Symbol == "NUMBER"
                ? From(new Num(BigInteger.Parse(ch[0].Text!, CultureInfo.InvariantCulture)), ch[0])
                : From(new Var(ch[0].Text!), ch[0]);
        }

        return ch[0].Symbol == "MINUS"
            ? From(new Neg(Expr(ch[1])), node)
            : From(Expr(ch[1]), node);
    }

    private BExpr Cond(ParseNode node)
    {
        if (node.Symbol == "literal")
        {
            return Literal(node);
        }

        var ch = node.Children;
        var left = Cond(ch[0]);
        for (var i = 1; i < ch.Count; i += 2)
        {
            var right = Cond(ch[i + 1]);
            left = new BoolOp(Op(ch[i].Text!), left, right) { Span = _index.Span(left.Span.Start, right.Span.End) };
        }

        return ch.Count > 1 ? From(left, node) : left;
    }

    private BExpr Literal(ParseNode node)
    {
        var ch = node.Children;
        return ch[0].Symbol switch
        {
            "NOT" => From(new Not(Literal(ch[1])), node),
            "TRUE" or "FALSE" => From(new BoolConst(ch[0].Symbol == "TRUE"), node),
            "LPAR" => From(Cond(ch[1]), node),
            _ => From(new Compare(Op(ch[1].Text!), Expr(ch[0]), Expr(ch[2])), node),
        };
    }
}
