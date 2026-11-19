using System.Text;
using System.Text.Json.Nodes;
using Sipl.Ast;
using Sipl.Syntax;

namespace Sipl.Visualization;

public sealed class TreeComparison
{
    private readonly Dictionary<int, int> _parseToAst = [];

    private TreeComparison(ParseResult result)
    {
        Source = result.Source;
        Tree = result.Tree!;
        Program = result.Program!;

        var byOrigin = new Dictionary<ParseNode, Node>(ReferenceEqualityComparer.Instance as IEqualityComparer<ParseNode>);
        foreach (var (node, origin) in result.Origins!)
        {
            if (!byOrigin.ContainsKey(origin) || node.Span == origin.Span)
            {
                byOrigin[origin] = node;
            }
        }

        Map(Tree, null, byOrigin);
    }

    public string Source { get; }
    public ParseNode Tree { get; }
    public Ast.Program Program { get; }

    public IReadOnlyDictionary<int, int> ParseToAst => _parseToAst;

    public static TreeComparison Build(ParseResult result) => new(result);

    public static TreeComparison Build(string source)
    {
        var r = Parser.ParseSource(source);
        return r.Ok ? new(r) : throw new Diagnostics.SiplSyntaxException(r.Diagnostics[0]);
    }

    private void Map(ParseNode p, Node? parent, Dictionary<ParseNode, Node> byOrigin)
    {
        var cur = p;
        while (!byOrigin.ContainsKey(cur) && cur.Children.Count == 1)
        {
            cur = cur.Children[0];
        }

        var target = byOrigin.TryGetValue(cur, out var n) && cur.Span == p.Span ? n : parent;
        if (target is not null)
        {
            _parseToAst[p.Id] = target.Id;
        }

        foreach (var c in p.Children)
        {
            Map(c, target, byOrigin);
        }
    }

    public List<ParseNode> ParseNodesOf(int astId) =>
        Tree.Walk().Where(p => _parseToAst.TryGetValue(p.Id, out var a) && a == astId).ToList();

    public string ToText()
    {
        var parseCount = Tree.Walk().Count();
        var astCount = Program.Walk().Count();
        var sb = new StringBuilder($"Вузлів у дереві виводу: {parseCount}, в AST: {astCount}\n\n");

        var rows = new List<(string Left, string Span, string From)>();

        void Rec(VisNode n, string prefix, string childPrefix)
        {
            var nodes = ParseNodesOf(n.Id);
            bool InChain(ParseNode p) => !p.IsTerminal || (n.Children.Count == 0 && p == nodes[^1]);
            var rules = string.Join(" ▸ ", nodes.Where(InChain).Select(p => p.Symbol));
            var tokens = string.Join(' ', nodes.Where(p => !InChain(p)).Select(p => $"'{p.Text}'"));
            rows.Add((prefix + (n.Role is null ? n.Label : $"{n.Role}: {n.Label}"), n.Span.ToString(),
                tokens.Length > 0 ? $"{rules}  + {tokens}" : rules));
            for (var i = 0; i < n.Children.Count; i++)
            {
                var last = i == n.Children.Count - 1;
                Rec(n.Children[i], childPrefix + (last ? "└── " : "├── "), childPrefix + (last ? "    " : "│   "));
            }
        }

        Rec(VisNode.FromAst(Program), "", "");
        var w1 = rows.Max(r => r.Left.Length) + 2;
        var w2 = rows.Max(r => r.Span.Length) + 2;
        sb.Append("AST".PadRight(w1)).Append("позиція".PadRight(w2)).Append("дерево виводу\n");
        foreach (var (left, span, from) in rows)
        {
            sb.Append(left.PadRight(w1)).Append(span.PadRight(w2)).Append(from).Append('\n');
        }

        return sb.ToString();
    }

    public string ToHtml(string title)
    {
        var parseVis = VisNode.FromParseTree(Tree);
        var astVis = VisNode.FromAst(Program);

        static JsonObject Index(VisNode root)
        {
            var obj = new JsonObject();
            foreach (var n in root.Walk())
            {
                obj[n.Id.ToString()] = new JsonObject { ["start"] = n.Span.Start, ["end"] = n.Span.End };
            }

            return obj;
        }

        var p2a = new JsonObject();
        foreach (var (p, a) in _parseToAst)
        {
            p2a[p.ToString()] = a;
        }

        string Script(JsonNode node) => Json.Serialize(node, indented: false).Replace("</", "<\\/");

        return $$"""
            <!DOCTYPE html>
            <html lang="uk">
            <head>
            <meta charset="utf-8">
            <title>{{TreeRenderer.Html(title)}}</title>
            <style>
              body { margin: 0; font-family: Helvetica, Arial, sans-serif; }
              main { display: grid; grid-template-columns: 22% 1fr 1fr; height: 100vh; }
              section { overflow: auto; border-right: 1px solid #ddd; }
              h2 { margin: 0; padding: 6px 10px; font-size: 14px; background: #f5f5f5; }
              pre { margin: 0; padding: 10px; font: 13px/1.5 Consolas, monospace; }
              mark { background: #fdd663; }
            </style>
            </head>
            <body>
            <main>
            <section><h2>Програма</h2><pre id="src"></pre></section>
            <section id="parse"><h2>Дерево виводу</h2>{{TreeRenderer.ToSvg(parseVis, Source, "Дерево виводу", "p")}}</section>
            <section id="ast"><h2>AST</h2>{{TreeRenderer.ToSvg(astVis, Source, "AST", "a")}}</section>
            </main>
            <script>
            const SOURCE = {{Script(JsonValue.Create(Source))}};
            const P = {{Script(Index(parseVis))}};
            const A = {{Script(Index(astVis))}};
            const P2A = {{Script(p2a)}};
            const src = document.getElementById('src');
            const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;');
            function show(n) {
              src.innerHTML = esc(SOURCE.slice(0, n.start)) + '<mark>' + esc(SOURCE.slice(n.start, n.end)) + '</mark>' + esc(SOURCE.slice(n.end));
            }
            function mark(id, cls) { const g = document.getElementById(id); if (g) g.classList.add(cls); }
            function clear() { document.querySelectorAll('.sel, .match').forEach(g => g.classList.remove('sel', 'match')); }
            document.querySelectorAll('#parse .node').forEach(g => g.addEventListener('mouseenter', () => {
              clear(); mark('p' + g.dataset.id, 'sel'); mark('a' + P2A[g.dataset.id], 'match'); show(P[g.dataset.id]);
            }));
            document.querySelectorAll('#ast .node').forEach(g => g.addEventListener('mouseenter', () => {
              clear(); mark('a' + g.dataset.id, 'sel');
              for (const [p, a] of Object.entries(P2A)) if (String(a) === g.dataset.id) mark('p' + p, 'match');
              show(A[g.dataset.id]);
            }));
            src.textContent = SOURCE;
            </script>
            </body>
            </html>

            """;
    }
}
