using System.Text.RegularExpressions;
using Sipl.Syntax;

namespace Sipl.Tests;

public class GrammarTests
{
    public static IEnumerable<object[]> Valid() =>
        Directory.GetFiles(TestPaths.Combine("tests", "grammar", "valid"), "*.sipl").Order().Select(f => new object[] { f });

    public static IEnumerable<object[]> Invalid() =>
        Directory.GetFiles(TestPaths.Combine("tests", "grammar", "invalid"), "*.sipl").Order().Select(f => new object[] { f });

    [Fact]
    public void AtLeastTenOfEach()
    {
        Assert.True(Valid().Count() >= 10);
        Assert.True(Invalid().Count() >= 10);
    }

    [Theory]
    [MemberData(nameof(Valid))]
    public void ValidProgramParses(string file)
    {
        var r = Parser.ParseSource(File.ReadAllText(file));
        Assert.True(r.Ok, r.Diagnostics.FirstOrDefault()?.Message);
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void InvalidProgramGivesErrorAtRightPlace(string file)
    {
        var expected = File.ReadAllText(Path.ChangeExtension(file, ".expect")).Trim();
        var r = Parser.ParseSource(File.ReadAllText(file));
        Assert.False(r.Ok);
        var d = r.Diagnostics[0];
        Assert.Equal(expected, $"{d.Code} {d.Span!.Value.Line}:{d.Span.Value.Column}");
    }

    [Theory]
    [InlineData("if")]
    [InlineData("while")]
    [InlineData("do")]
    [InlineData("begin")]
    [InlineData("end")]
    [InlineData("skip")]
    [InlineData("true")]
    [InlineData("not")]
    public void KeywordCannotBeVariable(string word) => Assert.False(Parser.ParseSource($"{word} := 1").Ok);

    [Theory]
    [InlineData("iffy")]
    [InlineData("endx")]
    [InlineData("x_1")]
    public void NameStartingWithKeywordIsVariable(string name) => Assert.True(Parser.ParseSource($"{name} := 1").Ok);

    [Fact]
    public void GrammarFileMatchesParser()
    {
        var ebnf = File.ReadAllText(TestPaths.Combine("grammar", "sipl.ebnf"));
        var rules = Regex.Matches(ebnf, @"^([a-z_]+)\s*=", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).Where(r => r is not ("letter" or "digit")).Order().ToList();

        var used = Valid().SelectMany(f => Parser.ParseTree(File.ReadAllText((string)f[0])).Walk())
            .Where(n => !n.IsTerminal).Select(n => n.Symbol).Distinct().Order().ToList();

        Assert.Equal(rules, used);
    }
}
