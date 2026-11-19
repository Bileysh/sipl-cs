using System.Reflection;

namespace Sipl;

public static class SiplInfo
{
    public static string Version { get; } =
        typeof(SiplInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
}
