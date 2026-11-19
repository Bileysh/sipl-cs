using Sipl.Text;

namespace Sipl.Syntax;

public sealed class ParseNode
{
    public ParseNode(string symbol, SourceSpan span, IReadOnlyList<ParseNode> children)
    {
        Symbol = symbol;
        Span = span;
        Children = children;
    }

    public ParseNode(string symbol, SourceSpan span, string text)
    {
        Symbol = symbol;
        Span = span;
        Text = text;
        Children = [];
    }

    public string Symbol { get; }

    public SourceSpan Span { get; }

    public IReadOnlyList<ParseNode> Children { get; }

    public string? Text { get; }

    public int Id { get; internal set; } = -1;

    public bool IsTerminal => Text is not null;

    public IEnumerable<ParseNode> Walk() => Children.SelectMany(c => c.Walk()).Prepend(this);
}
