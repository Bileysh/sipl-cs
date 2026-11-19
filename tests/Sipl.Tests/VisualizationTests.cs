using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Sipl.Cli;
using Sipl.Syntax;
using Sipl.Visualization;

namespace Sipl.Tests;

public class VisualizationTests
{
    private const string Src = "begin\n R := 1;\n while Y > 0 do\n begin\n  R := R * X;\n  Y := Y - 1\n end\nend";

    private static VisNode Tree(string src = Src) => VisNode.FromParseTree(Parser.ParseTree(src));

    [Fact]
    public void Text()
    {
        var text = TreeRenderer.ToText(Tree(), Src);
        Assert.StartsWith("program  [1:1-8:4]", text);
        Assert.Contains("while_stmt  [3:2-7:5]", text);
        Assert.Contains("'while'", text);
        Assert.Contains("\"R := R * X\"", text);
    }

    [Fact]
    public void JsonNodesMatchSource()
    {
        var root = JsonNode.Parse(TreeRenderer.Render(Tree(), Src, "json", "t"))!["root"]!;
        var all = new List<JsonNode>();
        void Walk(JsonNode n)
        {
            all.Add(n);
            foreach (var c in n["children"]?.AsArray() ?? [])
            {
                Walk(c!);
            }
        }

        Walk(root);
        foreach (var n in all)
        {
            Assert.Equal(Src[(int)n["span"]!["start"]!..(int)n["span"]!["end"]!], (string?)n["text"]);
        }

        var leaves = all.Where(n => (string?)n["category"] == "terminal").Select(n => (string)n["label"]!);
        Assert.Equal(Regex.Replace(Src, @"\s", ""), string.Concat(leaves));
    }

    [Fact]
    public void Dot()
    {
        var dot = TreeRenderer.ToDot(Tree(), Src, "t");
        Assert.StartsWith("digraph \"t\"", dot);
        Assert.Contains("tooltip=\"3:2-7:5: while Y > 0 do", dot);
        Assert.Equal(Regex.Matches(dot, @"^\s+n\d+ \[", RegexOptions.Multiline).Count, Regex.Matches(dot, "->").Count + 1);
    }

    [Fact]
    public void SvgIsValidAndNodesHaveSpans()
    {
        var svg = XDocument.Parse(TreeRenderer.ToSvg(Tree(), Src, "t"));
        var groups = svg.Descendants(XName.Get("g", "http://www.w3.org/2000/svg")).ToList();
        Assert.Equal(Parser.ParseTree(Src).Walk().Count(), groups.Count);
        Assert.All(groups, g => Assert.True(int.Parse(g.Attribute("data-end")!.Value) <= Src.Length));
    }

    [Fact]
    public void SvgNodesDoNotOverlap()
    {
        var rects = Regex.Matches(TreeRenderer.ToSvg(Tree(), Src, "t"), "<rect x=\"([\\d.]+)\" y=\"([\\d.]+)\" width=\"([\\d.]+)\"");
        foreach (var level in rects.GroupBy(m => m.Groups[2].Value))
        {
            var spans = level.Select(m => (A: double.Parse(m.Groups[1].Value), W: double.Parse(m.Groups[3].Value))).OrderBy(s => s.A).ToList();
            for (var i = 1; i < spans.Count; i++)
            {
                Assert.True(spans[i - 1].A + spans[i - 1].W <= spans[i].A + 0.1);
            }
        }
    }

    [Fact]
    public void AstView()
    {
        var text = TreeRenderer.ToText(VisNode.FromAst(Parser.Parse(Src)), Src);
        Assert.Contains("cond: >", text);
        Assert.Contains("body: begin…end", text);
        Assert.Contains("[label=\"left\"]", TreeRenderer.ToDot(VisNode.FromAst(Parser.Parse(Src)), Src, "t"));
    }

    [Fact]
    public void EveryParseNodeMapsToAstNode()
    {
        var cmp = TreeComparison.Build(Src);
        Assert.All(cmp.Tree.Walk(), p => Assert.True(cmp.ParseToAst.ContainsKey(p.Id), p.Symbol));
    }

    [Fact]
    public void ChainCollapsesIntoOneNode()
    {
        var cmp = TreeComparison.Build("x := 7");
        var num = cmp.Program.Walk().OfType<Ast.Num>().Single();
        Assert.Equal(["expr", "term", "factor", "NUMBER"], cmp.ParseNodesOf(num.Id).Select(p => p.Symbol));
    }

    [Fact]
    public void ParenthesesBelongToTheirExpression()
    {
        var cmp = TreeComparison.Build("x := (a + 1) * 2");
        var plus = cmp.Program.Walk().OfType<Ast.BinOp>().Single(b => b.Op == "+");
        Assert.Equal(["(", "+", ")"], cmp.ParseNodesOf(plus.Id).Where(p => p.IsTerminal).Select(p => p.Text));
    }

    [Fact]
    public void ComparisonText()
    {
        var text = TreeComparison.Build(Src).ToText();
        Assert.Contains("expr ▸ term ▸ factor ▸ NUMBER", text);
        Assert.Contains("statement ▸ assign  + 'R' ':='", text);
    }

    [Fact]
    public void ComparisonHtml()
    {
        var html = TreeComparison.Build("x := 1 { </script> }").ToHtml("t");
        Assert.Equal(2, Regex.Matches(html, "<svg ").Count);
        Assert.Contains("id=\"p0\"", html);
        Assert.Contains("id=\"a0\"", html);
        Assert.Contains("const P2A", html);
        Assert.DoesNotContain("</script> }", html);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("json")]
    [InlineData("dot")]
    [InlineData("svg")]
    public void TreeCommand(string format) =>
        Assert.Equal(0, CliRunner.Run("tree", TestPaths.Example("power.sipl"), "-f", format).Code);

    [Fact]
    public void TreeCommandOptions()
    {
        var file = Path.Combine(Path.GetTempPath(), $"sipl-{Guid.NewGuid():N}.html");
        Assert.Equal(0, CliRunner.Run("tree", TestPaths.Example("power.sipl"), "--compare", "-f", "html", "-o", file).Code);
        Assert.StartsWith("<!DOCTYPE html>", File.ReadAllText(file));
        Assert.Contains("value: 1", CliRunner.Run("tree", TestPaths.Example("power.sipl"), "--ast").Out);
        Assert.Contains("Вузлів у дереві виводу", CliRunner.Run("tree", TestPaths.Example("power.sipl"), "--compare").Out);
        Assert.Equal(ExitCodes.Usage, CliRunner.Run("tree", TestPaths.Example("power.sipl"), "-f", "html").Code);
        Assert.Equal(ExitCodes.Usage, CliRunner.Run("tree", TestPaths.Example("power.sipl"), "--compare", "-f", "svg").Code);
        Assert.Equal(ExitCodes.SyntaxError, CliRunner.Run("tree", TestPaths.Example("errors/missing_else.sipl")).Code);
    }
}
