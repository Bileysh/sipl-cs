using System.Text;
using System.Text.Json.Nodes;
using Sipl.Text;

namespace Sipl.Diagnostics;

public static class Codes
{
    public const string UnexpectedChar = "E001";
    public const string UnexpectedToken = "E002";
    public const string UnexpectedEof = "E003";
    public const string EmptyProgram = "E004";

    public const string UndefinedVariable = "R001";
    public const string DivisionByZero = "R002";
    public const string StepLimit = "R003";
}

public sealed record Diagnostic(string Code, string Message, SourceSpan? Span, string? Hint = null,
    IReadOnlyList<string>? Expected = null)
{
    public JsonObject ToJson() => new()
    {
        ["code"] = Code,
        ["severity"] = "error",
        ["message"] = Message,
        ["span"] = Span?.ToJson(),
        ["hint"] = Hint,
        ["expected"] = new JsonArray((Expected ?? []).Select(e => (JsonNode)JsonValue.Create(e)).ToArray()),
    };

    public string Format(string fileName, string? source = null)
    {
        var sb = new StringBuilder(Span is { } s ? $"{fileName}:{s.Line}:{s.Column}" : fileName);
        sb.Append($": error {Code}: {Message}");

        if (Span is { } span && source is not null)
        {
            var lines = source.Split('\n');
            if (span.Line <= lines.Length)
            {
                var width = span.EndLine == span.Line ? Math.Max(1, span.EndColumn - span.Column) : 1;
                sb.Append($"\n  {span.Line,4} | {lines[span.Line - 1].TrimEnd('\r')}");
                sb.Append($"\n       | {new string(' ', span.Column - 1)}{new string('^', width)}");
            }
        }

        if (Hint is not null)
        {
            sb.Append($"\n  підказка: {Hint}");
        }

        return sb.ToString();
    }
}

public class SiplException(Diagnostic diagnostic) : Exception(diagnostic.Message)
{
    public Diagnostic Diagnostic { get; } = diagnostic;
}

public sealed class SiplSyntaxException(Diagnostic diagnostic) : SiplException(diagnostic);

public class SiplRuntimeException(Diagnostic diagnostic) : SiplException(diagnostic);

public sealed class StepLimitExceededException(Diagnostic diagnostic) : SiplRuntimeException(diagnostic);
