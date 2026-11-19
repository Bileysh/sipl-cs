using System.Numerics;
using Sipl.Runtime;
using Sipl.Syntax;

namespace Sipl.Tests;

public class InterpreterTests
{
    private static RunResult Run(string src, string input = "", int maxSteps = Interpreter.DefaultMaxSteps) =>
        Interpreter.Run(Parser.Parse(src), TestPaths.State(input), maxSteps);

    [Theory]
    [InlineData("x := 2; y := x * x; x := y + x", "", "x=6 y=4")]
    [InlineData("skip", "a=1", "a=1")]
    [InlineData("begin x := 1; begin y := 2 end end", "", "x=1 y=2")]
    [InlineData("if x < 10 then y := 1 else y := 2", "x=5", "x=5 y=1")]
    [InlineData("if x < 10 then y := 1 else y := 2", "x=15", "x=15 y=2")]
    [InlineData("while i < 0 do i := i + 1", "i=5", "i=5")]
    [InlineData("while i < 3 do i := i + 1; y := i", "i=0", "i=3 y=3")]
    public void Statements(string src, string input, string expected) =>
        Assert.Equal(expected, TestPaths.Show(Run(src, input).State));

    [Theory]
    [InlineData("1 + 2 * 3", "7")]
    [InlineData("(1 + 2) * 3", "9")]
    [InlineData("10 - 3 - 2", "5")]
    [InlineData("7 / 2", "3")]
    [InlineData("-7 / 2", "-4")]
    [InlineData("7 % 3", "1")]
    [InlineData("-7 % 3", "2")]
    [InlineData("7 % -3", "-2")]
    [InlineData("--4", "4")]
    [InlineData("99999999999 * 99999999999", "9999999999800000000001")]
    public void Arithmetic(string expr, string value) =>
        Assert.Equal(BigInteger.Parse(value), Run($"r := {expr}").State["r"]);

    [Theory]
    [InlineData("1 < 2", true)]
    [InlineData("2 <= 2", true)]
    [InlineData("3 > 4", false)]
    [InlineData("4 >= 5", false)]
    [InlineData("1 = 1", true)]
    [InlineData("1 <> 1", false)]
    [InlineData("true and false", false)]
    [InlineData("false or not false and true", true)]
    [InlineData("¬(1 < 2) ∨ 2 ≤ 2", true)]
    public void Conditions(string cond, bool value) =>
        Assert.Equal(value ? 1 : 0, (int)Run($"if {cond} then r := 1 else r := 0").State["r"]);

    [Fact]
    public void FloorDivisionAgreesWithRemainder()
    {
        for (var a = -7; a <= 7; a++)
        {
            foreach (var b in new[] { -3, -2, 2, 3 })
            {
                Assert.Equal(a, (int)(Evaluator.FloorDiv(a, b) * b + Evaluator.FloorMod(a, b)));
            }
        }
    }

    [Fact]
    public void InputIsNotChanged()
    {
        var input = TestPaths.State("Y=3");
        Interpreter.Run(Parser.Parse("Y := 0"), input);
        Assert.Equal(3, (int)input["Y"]);
    }

    [Fact]
    public void UndefinedVariable()
    {
        var r = Run("x := 1;\ny := x + z");
        Assert.Equal(RunStatus.RuntimeError, r.Status);
        Assert.Equal(("R001", 2, 10), (r.Diagnostic!.Code, r.Diagnostic.Span!.Value.Line, r.Diagnostic.Span.Value.Column));
        Assert.Equal("x=1", TestPaths.Show(r.State));
    }

    [Theory]
    [InlineData("x := 1 / (y - y)")]
    [InlineData("x := 1 % 0")]
    public void DivisionByZero(string src) => Assert.Equal("R002", Run(src, "y=3").Diagnostic!.Code);

    [Fact]
    public void OrEvaluatesBothSides() => Assert.Equal(RunStatus.RuntimeError, Run("if true or u > 0 then skip else skip").Status);

    [Fact]
    public void InfiniteLoopStopsAtLimit()
    {
        var r = Run("x := 0;\nwhile x >= 0 do x := x + 1", maxSteps: 1000);
        Assert.Equal(RunStatus.StepLimit, r.Status);
        Assert.Equal("R003", r.Diagnostic!.Code);
        Assert.Equal(2, r.Diagnostic.Span!.Value.Line);
        Assert.Equal(1001, r.Steps);
        Assert.Equal(RunStatus.StepLimit, Run("while true do skip", maxSteps: 10).Status);
    }

    [Fact]
    public void LimitMustBePositive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Interpreter(Parser.Parse("skip"), maxSteps: 0));

    [Theory]
    [InlineData("power", "X=2 Y=10", "R=1024")]
    [InlineData("power", "X=7 Y=0", "R=1")]
    [InlineData("factorial", "N=5", "F=120")]
    [InlineData("gcd", "A=84 B=36", "A=12")]
    [InlineData("fibonacci", "N=20", "F=6765")]
    [InlineData("isqrt", "N=50", "R=7")]
    [InlineData("divmod", "X=17 Y=5", "Q=3 R=2")]
    [InlineData("max3", "A=3 B=9 C=4", "M=9")]
    [InlineData("prime", "N=97", "P=1")]
    [InlineData("prime", "N=91", "P=0")]
    [InlineData("collatz", "N=27", "S=111")]
    [InlineData("digits_sum", "N=2026", "S=10")]
    public void Examples(string name, string input, string expected)
    {
        var r = Interpreter.Run(Parser.Parse(File.ReadAllText(TestPaths.Example(name + ".sipl"))), TestPaths.State(input));
        Assert.True(r.Ok);
        foreach (var (k, v) in TestPaths.State(expected))
        {
            Assert.Equal(v, r.State[k]);
        }
    }

    [Fact]
    public void ResultJson()
    {
        var json = Run("x := 5").ToJson();
        Assert.Equal("ok", (string?)json["status"]);
        Assert.Equal(5, (int)json["state"]!["x"]!);
        Assert.Null(json["diagnostic"]);
    }
}
