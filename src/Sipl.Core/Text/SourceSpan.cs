using System.Text.Json.Nodes;

namespace Sipl.Text;

public readonly record struct SourceSpan(int Start, int End, int Line, int Column, int EndLine, int EndColumn)
{
    public string Slice(string source) => source[Start..End];

    public override string ToString() => $"{Line}:{Column}-{EndLine}:{EndColumn}";

    public JsonObject ToJson() => new()
    {
        ["start"] = Start,
        ["end"] = End,
        ["line"] = Line,
        ["column"] = Column,
        ["end_line"] = EndLine,
        ["end_column"] = EndColumn,
    };
}

public sealed class LineIndex
{
    private readonly List<int> _lineStarts = [0];

    public LineIndex(string source)
    {
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n')
            {
                _lineStarts.Add(i + 1);
            }
        }
    }

    public (int Line, int Column) Position(int offset)
    {
        var idx = _lineStarts.BinarySearch(offset);
        var line = idx >= 0 ? idx : ~idx - 1;
        return (line + 1, offset - _lineStarts[line] + 1);
    }

    public SourceSpan Span(int start, int end)
    {
        var (line, col) = Position(start);
        var (endLine, endCol) = Position(end);
        return new SourceSpan(start, end, line, col, endLine, endCol);
    }
}
