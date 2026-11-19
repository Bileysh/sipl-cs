using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Sipl.Visualization;

public static class TreeRenderer
{
    public static readonly string[] Formats = ["text", "json", "dot", "svg"];

    private static readonly Dictionary<string, (string Fill, string Stroke)> Colors = new()
    {
        ["nonterminal"] = ("#e8f0fe", "#3367d6"),
        ["terminal"] = ("#fef7e0", "#b06000"),
        ["statement"] = ("#e8f0fe", "#3367d6"),
        ["expression"] = ("#e6f4ea", "#137333"),
        ["condition"] = ("#fce8e6", "#c5221f"),
    };

    public static string Render(VisNode root, string source, string format, string title) => format switch
    {
        "text" => ToText(root, source) + "\n",
        "json" => Json.Serialize(new JsonObject { ["source"] = source, ["root"] = root.ToJson(source) }) + "\n",
        "dot" => ToDot(root, source, title),
        "svg" => ToSvg(root, source, title),
        _ => throw new ArgumentException($"невідомий формат {format}"),
    };

    private static string Short(string text, int limit = 40)
    {
        var t = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return t.Length <= limit ? t : t[..(limit - 1)] + "…";
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    internal static string Html(string s) => WebUtility.HtmlEncode(s);

    public static string ToText(VisNode root, string source)
    {
        var lines = new List<string>();

        void Rec(VisNode n, string prefix, string childPrefix)
        {
            var label = n.Category == "terminal"
                ? $"'{n.Label}'"
                : (n.Role is null ? n.Label : $"{n.Role}: {n.Label}") + $"  [{n.Span}]"
                  + (n.Children.Count > 0 ? $"  \"{Short(n.Span.Slice(source))}\"" : "");
            lines.Add(prefix + label);
            for (var i = 0; i < n.Children.Count; i++)
            {
                var last = i == n.Children.Count - 1;
                Rec(n.Children[i], childPrefix + (last ? "└── " : "├── "), childPrefix + (last ? "    " : "│   "));
            }
        }

        Rec(root, "", "");
        return string.Join('\n', lines);
    }

    public static string ToDot(VisNode root, string source, string title)
    {
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        var sb = new StringBuilder($"digraph \"{Esc(title)}\" {{\n");
        sb.Append("  graph [ordering=out];\n  node [fontname=\"Helvetica\", fontsize=11, style=filled];\n");
        foreach (var n in root.Walk())
        {
            var (fill, stroke) = Colors[n.Category];
            var shape = n.Category == "terminal" ? "box" : "ellipse";
            var tooltip = $"{n.Span}: {Short(n.Span.Slice(source), 80)}";
            sb.Append($"  n{n.Id} [label=\"{Esc(n.Label)}\", shape={shape}, fillcolor=\"{fill}\", color=\"{stroke}\", tooltip=\"{Esc(tooltip)}\"];\n");
        }

        foreach (var n in root.Walk())
        {
            foreach (var c in n.Children)
            {
                sb.Append(c.Role is null ? $"  n{n.Id} -> n{c.Id};\n" : $"  n{n.Id} -> n{c.Id} [label=\"{c.Role}\"];\n");
            }
        }

        return sb.Append("}\n").ToString();
    }

    private const double CharW = 7.2, NodeH = 26, LevelH = 64, Gap = 12, Pad = 20;

    private sealed class Box(double x, double y, double w)
    {
        public double X { get; set; } = x;
        public double Y { get; } = y;
        public double W { get; } = w;
    }

    private static Dictionary<VisNode, Box> Layout(VisNode root)
    {
        Dictionary<VisNode, Box> boxes = new(ReferenceEqualityComparer.Instance as IEqualityComparer<VisNode>);
        var cursor = Pad;

        void Place(VisNode n, int depth)
        {
            var w = Math.Max(30, n.Label.Length * CharW + 16);
            var y = Pad + depth * LevelH;
            var before = cursor;
            if (n.Children.Count == 0)
            {
                boxes[n] = new Box(before + w / 2, y, w);
                cursor = before + w + Gap;
                return;
            }

            foreach (var c in n.Children)
            {
                Place(c, depth + 1);
            }

            var x = (boxes[n.Children[0]].X + boxes[n.Children[^1]].X) / 2;
            boxes[n] = new Box(x, y, w);
            if (x - w / 2 < before)
            {
                var dx = before - (x - w / 2);
                foreach (var m in n.Walk())
                {
                    boxes[m].X += dx;
                }

                cursor += dx;
            }

            cursor = Math.Max(cursor, boxes[n].X + w / 2 + Gap);
        }

        Place(root, 0);
        return boxes;
    }

    public static string ToSvg(VisNode root, string source, string title, string idPrefix = "n")
    {
        var boxes = Layout(root);
        var width = boxes.Values.Max(b => b.X + b.W / 2) + Pad;
        var height = boxes.Values.Max(b => b.Y) + NodeH + Pad;
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(width)}\" height=\"{F(height)}\" font-family=\"Helvetica, Arial, sans-serif\" font-size=\"12\">\n");
        sb.Append($"<title>{Html(title)}</title>\n");
        sb.Append("<style>.node{cursor:pointer}.node:hover rect,.node.sel rect{stroke-width:3}.node.match rect{stroke-width:3;stroke-dasharray:4 2}.role{fill:#80868b;font-size:10px}</style>\n");

        foreach (var (n, b) in boxes)
        {
            foreach (var c in n.Children)
            {
                var cb = boxes[c];
                var mid = (b.Y + NodeH + cb.Y) / 2;
                sb.Append($"<path d=\"M{F(b.X)},{F(b.Y + NodeH)} C{F(b.X)},{F(mid)} {F(cb.X)},{F(mid)} {F(cb.X)},{F(cb.Y)}\" stroke=\"#9aa0a6\" fill=\"none\"/>\n");
                if (c.Role is not null)
                {
                    sb.Append($"<text class=\"role\" x=\"{F((b.X + cb.X) / 2 + 4)}\" y=\"{F(mid)}\">{c.Role}</text>\n");
                }
            }
        }

        foreach (var (n, b) in boxes)
        {
            var (fill, stroke) = Colors[n.Category];
            sb.Append($"<g class=\"node\" id=\"{idPrefix}{n.Id}\" data-id=\"{n.Id}\" data-start=\"{n.Span.Start}\" data-end=\"{n.Span.End}\">");
            sb.Append($"<title>{Html($"{n.Span}  {Short(n.Span.Slice(source), 80)}")}</title>");
            sb.Append($"<rect x=\"{F(b.X - b.W / 2)}\" y=\"{F(b.Y)}\" width=\"{F(b.W)}\" height=\"{F(NodeH)}\" rx=\"{(n.Category == "terminal" ? 3 : 13)}\" fill=\"{fill}\" stroke=\"{stroke}\"/>");
            sb.Append($"<text x=\"{F(b.X)}\" y=\"{F(b.Y + 17)}\" text-anchor=\"middle\">{Html(n.Label)}</text></g>\n");
        }

        return sb.Append("</svg>\n").ToString();
    }
}
