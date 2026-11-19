using Sipl.Ast;
using Sipl.Diagnostics;
using Sipl.Syntax;

namespace Sipl.Tests;

public class ParserTests
{
    private const string Power = "begin\n R := 1;\n while Y > 0 do\n begin\n  R := R * X;\n  Y := Y - 1\n end\nend";

    private static string Ast(string src) => Json.Serialize(Parser.Parse(src).ToJson(withSpans: false), indented: false);

    [Fact]
    public void PowerProgramStructure()
    {
        var block = Assert.IsType<Block>(Parser.Parse(Power).Body);
        var seq = Assert.IsType<Seq>(block.Body);
        Assert.Equal("R", Assert.IsType<Assign>(seq.Body[0]).Target);
        var loop = Assert.IsType<While>(seq.Body[1]);
        Assert.Equal(">", Assert.IsType<Compare>(loop.Cond).Op);
    }

    [Fact]
    public void SpansPointToSource()
    {
        var loop = (While)((Seq)((Block)Parser.Parse(Power).Body).Body).Body[1];
        Assert.Equal((3, 2), (loop.Span.Line, loop.Span.Column));
        Assert.StartsWith("while Y > 0 do", loop.Span.Slice(Power));
        var assign = (Assign)((Seq)((Block)loop.Body).Body).Body[0];
        Assert.Equal("R := R * X", assign.Span.Slice(Power));
    }

    [Fact]
    public void NodeIdsArePreorder()
    {
        var ids = Parser.Parse(Power).Walk().Select(n => n.Id).ToList();
        Assert.Equal(Enumerable.Range(0, ids.Count), ids);
    }

    [Fact]
    public void MultiplicationBindsTighterThanAddition()
    {
        var e = (BinOp)((Assign)Parser.Parse("x := 1 + 2 * 3").Body).Value;
        Assert.Equal("+", e.Op);
        Assert.Equal("*", Assert.IsType<BinOp>(e.Right).Op);
    }

    [Fact]
    public void SubtractionIsLeftAssociative()
    {
        var e = (BinOp)((Assign)Parser.Parse("x := 1 - 2 - 3").Body).Value;
        Assert.IsType<BinOp>(e.Left);
        Assert.IsType<Num>(e.Right);
    }

    [Fact]
    public void AndBindsTighterThanOr()
    {
        var c = (BoolOp)((If)Parser.Parse("if a < 1 or b < 2 and not c = 3 then skip else skip").Body).Cond;
        Assert.Equal("or", c.Op);
        Assert.Equal("and", Assert.IsType<BoolOp>(c.Right).Op);
    }

    [Fact]
    public void MathSymbolsGiveSameAst()
    {
        Assert.Equal(Ast("if a <= b and not c <> 1 then x := a / 2 else skip"),
            Ast("if a ≤ b ∧ ¬c ≠ 1 then x := a ÷ 2 else skip"));
    }

    [Theory]
    [InlineData("if (x + 1) < 3 then skip else skip", "Compare")]
    [InlineData("if (x < 1) and (y = 2) then skip else skip", "BoolOp")]
    [InlineData("if ((x)) = 1 then skip else skip", "Compare")]
    public void ParenthesisInCondition(string src, string kind) =>
        Assert.Equal(kind, ((If)Parser.Parse(src).Body).Cond.Kind);

    [Fact]
    public void ParenthesesAreNotInAstButInSpan()
    {
        const string src = "x := ((y))";
        var v = Assert.IsType<Var>(((Assign)Parser.Parse(src).Body).Value);
        Assert.Equal("((y))", v.Span.Slice(src));
    }

    [Fact]
    public void CommentsAreSkipped() =>
        Assert.Equal(Ast("x := 1; y := 2"), Ast("x := 1; { коментар } y := 2 // ще один"));

    [Fact]
    public void BigNumbersInJson() =>
        Assert.Contains("123456789012345678901234567890", Ast("x := 123456789012345678901234567890"));

    [Fact]
    public void ParseThrowsWithDiagnostic()
    {
        var e = Assert.Throws<SiplSyntaxException>(() => Parser.Parse("x = 1"));
        Assert.Equal("E002", e.Diagnostic.Code);
        Assert.Throws<SiplSyntaxException>(() => Parser.ParseTree("x = 1"));
    }

    [Theory]
    [InlineData("x = 1", "':='")]
    [InlineData("begin x := 1; end", "перед 'end'")]
    [InlineData("if x < 1 then skip; y := 1", "else")]
    [InlineData("if x < 1 then skip", "else")]
    [InlineData("x := 1 y := 2", "пропущено ';'")]
    [InlineData("do := 1", "ключове слово")]
    [InlineData("x := 1;", "у кінці програми")]
    [InlineData("x : 1", "':='")]
    [InlineData("", "skip")]
    public void ErrorHints(string src, string hint) => Assert.Contains(hint, Parser.ParseSource(src).Diagnostics[0].Hint);

    [Fact]
    public void ErrorFormatShowsLineAndCaret()
    {
        const string src = "begin\n  x := 1;\nend";
        var d = Parser.ParseSource(src).Diagnostics[0];
        var text = d.Format("p.sipl", src);
        Assert.Contains("p.sipl:3:1: error E002", text);
        Assert.Contains("^^^", text);
        Assert.Contains("'begin'", d.Expected!);
        Assert.Equal(3, (int)d.ToJson()["span"]!["line"]!);
    }

    [Fact]
    public void UnicodeLettersAreErrorsWithRightPosition()
    {
        var d = Parser.ParseSource("// коментар\nx := 1;\nзмінна := 2").Diagnostics[0];
        Assert.Equal(("E001", 3, 1), (d.Code, d.Span!.Value.Line, d.Span.Value.Column));
    }

    [Fact]
    public void ParseTreeKeepsAllTokens()
    {
        var tree = Parser.ParseTree("x := (1 + 2)");
        Assert.Equal("program", tree.Symbol);
        Assert.Equal(["x", ":=", "(", "1", "+", "2", ")"], tree.Walk().Where(n => n.IsTerminal).Select(n => n.Text));
        Assert.Contains("term", tree.Walk().Select(n => n.Symbol));
    }
}
