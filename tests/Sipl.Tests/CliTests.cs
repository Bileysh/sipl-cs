using System.Diagnostics;
using System.Text.Json.Nodes;
using Sipl.Cli;

namespace Sipl.Tests;

public sealed record CliRun(int Code, string Out, string Err)
{
    public JsonNode Json => JsonNode.Parse(Out)!;
}

public static class CliRunner
{
    public static CliRun Run(params string[] args) => RunWithInput("", args);

    public static CliRun RunWithInput(string input, params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = new Cli.Cli(new StringReader(input), o, e).Run(args);
        return new CliRun(code, o.ToString(), e.ToString());
    }

    public static (string Dll, string Config) Binary()
    {
        var dll = typeof(Cli.Cli).Assembly.Location;
        var config = Path.ChangeExtension(dll, ".runtimeconfig.json");
        if (!File.Exists(config))
        {
            config = Path.ChangeExtension(typeof(CliRunner).Assembly.Location, ".runtimeconfig.json");
        }

        return (dll, config);
    }

    public static CliRun Process(string? input, params string[] args)
    {
        var (dll, config) = Binary();
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "exec", "--runtimeconfig", config, dll }.Concat(args))
        {
            psi.ArgumentList.Add(a);
        }

        using var p = System.Diagnostics.Process.Start(psi)!;
        p.StandardInput.Write(input ?? "");
        p.StandardInput.Close();
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return new CliRun(p.ExitCode, output.Result, error.Result);
    }
}

public class CliTests
{
    private static string Ex(string name) => TestPaths.Example(name);

    private static string Temp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sipl-{Guid.NewGuid():N}.sipl");
        File.WriteAllText(path, content);
        return path;
    }

    private static JsonNode CheckEnvelope(CliRun r)
    {
        var schema = JsonNode.Parse(File.ReadAllText(TestPaths.Combine("docs", "schemas", "cli.schema.json")))!;
        var json = r.Json;
        foreach (var key in schema["required"]!.AsArray())
        {
            Assert.True(json.AsObject().ContainsKey((string)key!), $"немає поля {key}");
        }

        Assert.Equal("sipl.cli/1", (string?)json["schema"]);
        Assert.Equal(r.Code, (int)json["exit_code"]!);
        Assert.Equal(r.Code == 0, (bool)json["ok"]!);
        return json;
    }

    [Fact]
    public void ParseText()
    {
        var r = CliRunner.Run("parse", Ex("power.sipl"));
        Assert.Equal(0, r.Code);
        Assert.Contains("\"kind\": \"Program\"", r.Out);
    }

    [Fact]
    public void ParseJson()
    {
        var json = CheckEnvelope(CliRunner.Run("parse", Ex("power.sipl"), "--json"));
        Assert.Equal("Program", (string?)json["result"]!["ast"]!["kind"]);
        Assert.NotNull(json["result"]!["ast"]!["span"]);
        var noSpans = CheckEnvelope(CliRunner.Run("parse", Ex("power.sipl"), "--json", "--no-spans"));
        Assert.Null(noSpans["result"]!["ast"]!["span"]);
    }

    [Fact]
    public void Check()
    {
        Assert.Contains("синтаксис правильний", CliRunner.Run("check", Ex("power.sipl")).Out);
        var r = CliRunner.Run("check", Temp("begin\n  x := 1;\nend"));
        Assert.Equal(ExitCodes.SyntaxError, r.Code);
        Assert.Contains(":3:1: error E002", r.Err);
        var json = CheckEnvelope(CliRunner.Run("check", Temp("x = 1"), "--json"));
        Assert.Equal("E002", (string?)json["diagnostics"]![0]!["code"]);
        Assert.Equal(3, (int)json["diagnostics"]![0]!["span"]!["column"]!);
    }

    [Fact]
    public void RunText()
    {
        var r = CliRunner.Run("run", Ex("power.sipl"), "--set", "X=2", "Y=10");
        Assert.Equal(0, r.Code);
        Assert.Contains("R = 1024", r.Out);
    }

    [Fact]
    public void RunJson()
    {
        var json = CheckEnvelope(CliRunner.Run("run", Ex("max3.sipl"), "--set", "A=-5", "B=-9", "C=123456789012345678901234567890", "--json"));
        Assert.Equal("123456789012345678901234567890", json["result"]!["state"]!["M"]!.ToJsonString());
    }

    [Fact]
    public void RunErrors()
    {
        var undefined = CliRunner.Run("run", Ex("power.sipl"), "--set", "X=2", "--json");
        Assert.Equal(ExitCodes.RuntimeError, undefined.Code);
        Assert.Equal("R001", (string?)CheckEnvelope(undefined)["diagnostics"]![0]!["code"]);

        var division = CliRunner.Run("run", Ex("errors/division_by_zero.sipl"));
        Assert.Equal(ExitCodes.RuntimeError, division.Code);
        Assert.Contains("R002", division.Err);

        var loop = CliRunner.Run("run", Ex("errors/infinite_loop.sipl"), "--max-steps", "100");
        Assert.Equal(ExitCodes.StepLimit, loop.Code);
        Assert.Contains("R003", loop.Err);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("frobnicate a.sipl")]
    [InlineData("run a.sipl b.sipl")]
    [InlineData("run a.sipl --max-steps 0")]
    [InlineData("run a.sipl --max-steps")]
    [InlineData("run a.sipl --set")]
    [InlineData("run a.sipl --set X=abc")]
    [InlineData("run a.sipl --set 1X=2")]
    [InlineData("parse a.sipl --set X=1")]
    [InlineData("tree a.sipl -f pdf")]
    public void UsageErrors(string line)
    {
        var r = CliRunner.Run(line.Split(' '));
        Assert.Equal(ExitCodes.Usage, r.Code);
        Assert.Contains("помилка", r.Err);
    }

    [Fact]
    public void HelpVersionAndMissingFile()
    {
        Assert.Contains("Коди завершення", CliRunner.Run("--help").Out);
        Assert.Equal(ExitCodes.Usage, CliRunner.Run().Code);
        Assert.Equal($"sipl {SiplInfo.Version}", CliRunner.Run("--version").Out.Trim());
        Assert.Equal(ExitCodes.IoError, CliRunner.Run("run", "/nonexistent/x.sipl").Code);
        var json = CliRunner.Run("run", "/nonexistent/x.sipl", "--json");
        Assert.Equal("IO", (string?)CheckEnvelope(json)["diagnostics"]![0]!["code"]);
    }

    [Fact]
    public void Stdin() => Assert.Contains("x = 42", CliRunner.RunWithInput("x := 6 * 7", "run", "-").Out);

    [Fact]
    public void TraceText()
    {
        var r = CliRunner.Run("trace", Ex("gcd.sipl"), "--set", "A=12", "B=8");
        Assert.Equal(0, r.Code);
        Assert.Contains("A: 12 -> 4", r.Out);
        Assert.Contains("умова: false", r.Out);
        Assert.Contains(":5:8: error R002", CliRunner.Run("trace", Ex("errors/division_by_zero.sipl")).Err);
    }

    [Fact]
    public void TraceJson()
    {
        var json = CheckEnvelope(CliRunner.Run("trace", Ex("power.sipl"), "--set", "X=2", "Y=2", "--json"));
        var events = json["result"]!["events"]!.AsArray();
        Assert.Equal("ProgramStarted", (string?)events[0]!["type"]);
        Assert.Equal("ProgramFinished", (string?)events[^1]!["type"]);
    }

    [Fact]
    public void AsSeparateProcess()
    {
        var r = CliRunner.Process(null, "run", Ex("fibonacci.sipl"), "--set", "N=30", "--json");
        Assert.Equal(0, r.Code);
        Assert.Equal(832040, (int)CheckEnvelope(r)["result"]!["state"]!["F"]!);
        Assert.Equal(ExitCodes.SyntaxError, CliRunner.Process(null, "check", Ex("errors/missing_else.sipl")).Code);
        Assert.Equal(ExitCodes.StepLimit, CliRunner.Process(null, "run", Ex("errors/infinite_loop.sipl"), "--max-steps", "50").Code);
        Assert.Contains("x = 42", CliRunner.Process("x := 6 * 7", "run", "-").Out);
    }
}
