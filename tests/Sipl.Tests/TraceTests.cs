using Sipl.Ast;
using Sipl.Runtime;
using Sipl.Syntax;

namespace Sipl.Tests;

public class TraceTests
{
    private const string Power = "begin\n R := 1;\n while Y > 0 do\n begin\n  R := R * X;\n  Y := Y - 1\n end\nend";

    private static Trace TracePower() => Interpreter.Trace(Parser.Parse(Power), TestPaths.State("X=3 Y=2"));

    [Fact]
    public void StartsAndFinishes()
    {
        var events = TracePower().Events;
        Assert.IsType<ProgramStarted>(events[0]);
        Assert.IsType<ProgramFinished>(events[^1]);
        Assert.Equal(Enumerable.Range(0, events.Count), events.Select(e => e.Seq));
    }

    [Fact]
    public void BeforeAndAfterArePaired()
    {
        var stack = new Stack<Stmt>();
        foreach (var e in TracePower().Events)
        {
            if (e is BeforeStatement b)
            {
                stack.Push(b.Node);
                Assert.Equal(stack.Count - 1, b.Depth);
            }
            else if (e is AfterStatement a)
            {
                Assert.Same(stack.Pop(), a.Node);
            }
        }

        Assert.Empty(stack);
    }

    [Fact]
    public void VariableChanges() =>
        Assert.Equal(["R:⊥->1", "R:1->3", "Y:2->1", "R:3->9", "Y:1->0"],
            TracePower().Events.OfType<VariableChanged>().Select(e => $"{e.Name}:{e.Old?.ToString() ?? "⊥"}->{e.New}"));

    [Fact]
    public void LoopConditionChecks() =>
        Assert.Equal([true, true, false], TracePower().Events.OfType<ConditionEvaluated>().Select(e => e.Value));

    [Fact]
    public void JsonEventsReferToAstNodes()
    {
        var program = Parser.Parse(Power);
        var json = Interpreter.Trace(program, TestPaths.State("X=2 Y=1")).ToJson();
        Assert.Equal("sipl.trace/1", (string?)json["schema"]);
        var ids = program.Walk().Select(n => n.Id).ToList();
        foreach (var e in json["events"]!.AsArray())
        {
            if (e!["node_id"] is { } id)
            {
                Assert.Contains((int)id, ids);
            }
        }

        var changed = json["events"]!.AsArray().First(e => (string?)e!["type"] == "VariableChanged")!;
        Assert.Null(changed["old"]);
        Assert.Equal(1, (int)changed["new"]!);
        Assert.Equal(2, (int)json["result"]!["state"]!["R"]!);
    }

    [Fact]
    public void ErrorEndsTrace()
    {
        var last = Assert.IsType<ProgramFinished>(Interpreter.Trace(Parser.Parse("x := 1 / 0")).Events[^1]);
        Assert.Equal(RunStatus.RuntimeError, last.Result.Status);
        Assert.Equal("R002", (string?)last.ToJson()["diagnostic"]!["code"]);
    }

    [Fact]
    public void EventsAreLazy()
    {
        var it = new Interpreter(Parser.Parse("x := 1; while true do skip"), maxSteps: int.MaxValue);
        Assert.Equal(50, it.Events().Take(50).Count());
        Assert.Equal(1, (int)it.State["x"]);
    }
}
