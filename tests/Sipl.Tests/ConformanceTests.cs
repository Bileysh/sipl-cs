using Sipl.Cli;
using Sipl.Conformance;

namespace Sipl.Tests;

public class ConformanceTests
{
    private static readonly string Dir = TestPaths.Combine("conformance", "cases");
    private static readonly List<ConformanceCase> All = ConformanceCase.Discover(Dir);

    private static string SelfCommand()
    {
        var (dll, config) = CliRunner.Binary();
        return $"dotnet exec --runtimeconfig \"{config}\" \"{dll}\"";
    }

    public static IEnumerable<object[]> Names() => All.Select(c => new object[] { c.Name });

    private static ConformanceCase Case(string name) => All.Single(c => c.Name == name);

    private static string WrongImplementation(string output)
    {
        var dir = Directory.CreateTempSubdirectory("sipl-fake").FullName;
        var json = Path.Combine(dir, "out.json");
        File.WriteAllText(json, output);
        if (OperatingSystem.IsWindows())
        {
            var cmd = Path.Combine(dir, "fake.cmd");
            File.WriteAllText(cmd, $"@type \"{json}\"\r\n");
            return $"\"{cmd}\"";
        }

        var sh = Path.Combine(dir, "fake.sh");
        File.WriteAllText(sh, $"#!/bin/sh\ncat '{json}'\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return $"\"{sh}\"";
    }

    private const string Wrong = "{\"result\": {\"status\": \"ok\", \"state\": {\"x\": 0}, \"ast\": {\"kind\": \"Program\"}}, \"diagnostics\": []}";

    [Fact]
    public void SuiteCoversAllKinds()
    {
        Assert.True(All.Count >= 50);
        Assert.Equal(["ast", "conditions", "expressions", "programs", "runtime", "statements", "syntax"],
            All.Select(c => c.Name.Split('/')[0]).Distinct().Order());
        Assert.Equal(["ok", "runtime_error", "step_limit", "syntax_error"],
            All.Select(c => (string)c.Expect["status"]!).Distinct().Order());
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void BuiltinPasses(string name)
    {
        var r = ConformanceRunner.Check(Case(name), new BuiltinImplementation());
        Assert.True(r.Passed, string.Join("; ", r.Failures));
    }

    [Fact]
    public void SeparateProcessPasses()
    {
        var impl = new ProcessImplementation(SelfCommand());
        foreach (var name in new[] { "programs/power", "syntax/missing_else", "runtime/division_by_zero", "runtime/infinite_loop", "ast/left_assoc" })
        {
            var r = ConformanceRunner.Check(Case(name), impl);
            Assert.True(r.Passed, string.Join("; ", r.Failures));
        }
    }

    [Fact]
    public void WrongImplementationIsCaught()
    {
        var impl = new ProcessImplementation(WrongImplementation(Wrong));
        Assert.Contains(ConformanceRunner.Check(Case("statements/assign"), impl).Failures, f => f.StartsWith("state"));
        Assert.Contains(ConformanceRunner.Check(Case("syntax/empty"), impl).Failures, f => f.StartsWith("status"));
        Assert.Contains(ConformanceRunner.Check(Case("ast/unary_minus"), impl).Failures, f => f.StartsWith("ast"));
        Assert.Contains(ConformanceRunner.Check(Case("programs/fibonacci"), impl).Failures, f => f.StartsWith("state_subset"));
        Assert.Contains(ConformanceRunner.Check(Case("runtime/undefined_variable"), impl).Failures, f => f.StartsWith("error.code"));
        Assert.False(ConformanceRunner.Check(All[0], new ProcessImplementation(WrongImplementation("not json"))).Passed);
        Assert.False(ConformanceRunner.Check(All[0], new ProcessImplementation("/nonexistent/sipl")).Passed);
    }

    [Theory]
    [InlineData("dotnet sipl.dll", new[] { "dotnet", "sipl.dll" })]
    [InlineData("\"C:\\Program Files\\x.exe\" run", new[] { "C:\\Program Files\\x.exe", "run" })]
    [InlineData("python -m 'my sipl'", new[] { "python", "-m", "my sipl" })]
    public void SplitCommand(string command, string[] expected) =>
        Assert.Equal(expected, ProcessImplementation.SplitCommand(command));

    [Fact]
    public void Command()
    {
        var ok = CliRunner.Run("conformance", Dir, "-k", "statements/");
        Assert.Equal(0, ok.Code);
        Assert.Contains("Пройдено 11 з 11", ok.Out);

        var fail = CliRunner.Run("conformance", Dir, "-k", "statements/assign", "--impl", WrongImplementation(Wrong));
        Assert.Equal(1, fail.Code);
        Assert.Contains("FAIL statements/assign", fail.Out);

        Assert.Equal(ExitCodes.Usage, CliRunner.Run("conformance", "/nonexistent").Code);
        var bad = Directory.CreateTempSubdirectory("sipl-cases").FullName;
        File.WriteAllText(Path.Combine(bad, "x.json"), "{\"expect\": {\"status\": \"ok\"}}");
        Assert.Equal(ExitCodes.Usage, CliRunner.Run("conformance", bad).Code);
        File.WriteAllText(Path.Combine(bad, "x.sipl"), "skip");
        File.WriteAllText(Path.Combine(bad, "x.json"), "{ не json");
        Assert.Equal(ExitCodes.Usage, CliRunner.Run("conformance", bad).Code);
        Assert.Equal(1, CliRunner.Run("conformance", Directory.CreateTempSubdirectory().FullName).Code);
    }
}
