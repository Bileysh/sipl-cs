using System.Numerics;

namespace Sipl.Tests;

public static class TestPaths
{
    public static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var env = Environment.GetEnvironmentVariable("SIPL_REPO_ROOT");
        if (!string.IsNullOrEmpty(env))
        {
            return env;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir!.FullName, "Sipl.sln")))
        {
            dir = dir.Parent;
        }

        return dir.FullName;
    }

    public static string Combine(params string[] parts) => Path.Combine([Root, .. parts]);

    public static string Example(string name) => Combine("examples", name);

    public static Dictionary<string, BigInteger> State(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('='))
            .ToDictionary(p => p[0], p => BigInteger.Parse(p[1]));

    public static string Show(IReadOnlyDictionary<string, BigInteger> state) =>
        string.Join(' ', state.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));
}
