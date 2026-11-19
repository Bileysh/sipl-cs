using System.Globalization;
using System.Text.Json.Nodes;
using Sipl.Ast;
using Sipl.Syntax;
using Sipl.Text;

namespace Sipl.Visualization;

public sealed class VisNode(int id, string label, string category, SourceSpan span, string? role = null)
{
    public int Id { get; } = id;
    public string Label { get; } = label;
    public string Category { get; } = category;
    public SourceSpan Span { get; } = span;
    public string? Role { get; } = role;
    public List<VisNode> Children { get; } = [];

    public IEnumerable<VisNode> Walk() => Children.SelectMany(c => c.Walk()).Prepend(this);

    public JsonObject ToJson(string source)
    {
        var obj = new JsonObject
        {
            ["id"] = Id,
            ["label"] = Label,
            ["category"] = Category,
            ["span"] = Span.ToJson(),
            ["text"] = Span.Slice(source),
        };
        if (Role is not null)
        {
            obj["role"] = Role;
        }

        if (Children.Count > 0)
        {
            obj["children"] = new JsonArray(Children.Select(c => (JsonNode)c.ToJson(source)).ToArray());
        }

        return obj;
    }

    public static VisNode FromParseTree(ParseNode node)
    {
        if (node.IsTerminal)
        {
            return new VisNode(node.Id, node.Text!, "terminal", node.Span);
        }

        var vis = new VisNode(node.Id, node.Symbol, "nonterminal", node.Span);
        vis.Children.AddRange(node.Children.Select(FromParseTree));
        return vis;
    }

    public static VisNode FromAst(Node node, string? role = null)
    {
        var (label, category) = node switch
        {
            Ast.Program => ("Program", "statement"),
            Assign a => ($"{a.Target} :=", "statement"),
            Seq => (";", "statement"),
            If => ("if", "statement"),
            While => ("while", "statement"),
            Block => ("begin…end", "statement"),
            Skip => ("skip", "statement"),
            Num n => (n.Value.ToString(CultureInfo.InvariantCulture), "expression"),
            Var v => (v.Name, "expression"),
            Neg => ("−", "expression"),
            BinOp b => (b.Op, "expression"),
            BoolConst c => (c.Value ? "true" : "false", "condition"),
            Compare c => (c.Op, "condition"),
            Not => ("not", "condition"),
            BoolOp b => (b.Op, "condition"),
            _ => (node.Kind, "statement"),
        };
        string[]? roles = node switch
        {
            If => ["cond", "then", "else"],
            While => ["cond", "body"],
            BinOp or Compare or BoolOp => ["left", "right"],
            Assign => ["value"],
            _ => null,
        };

        var vis = new VisNode(node.Id, label, category, node.Span, role);
        vis.Children.AddRange(node.Children.Select((c, i) => FromAst(c, roles?[i])));
        return vis;
    }
}
