using Sipl.Diagnostics;
using Sipl.Text;

namespace Sipl.Syntax;

public sealed record ParseResult(string Source, Ast.Program? Program, ParseNode? Tree, IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyDictionary<Ast.Node, ParseNode>? Origins = null)
{
    public bool Ok => Program is not null;
}

public sealed class Parser
{
    private readonly string _source;
    private readonly List<Token> _tokens;
    private readonly LineIndex _index;
    private int _pos;

    private int _farPos = -1;
    private readonly HashSet<TokenKind> _farExpected = [];

    private Parser(string source)
    {
        _source = source;
        _tokens = Lexer.Tokenize(source);
        _index = new LineIndex(source);
    }

    public static ParseResult ParseSource(string source)
    {
        var parser = new Parser(source);
        ParseNode tree;
        try
        {
            tree = parser.ProgramRule();
        }
        catch (ParseFailure)
        {
            return new ParseResult(source, null, null, [parser.BuildDiagnostic()]);
        }

        var id = 0;
        foreach (var n in tree.Walk())
        {
            n.Id = id++;
        }

        var builder = new AstBuilder(source);
        return new ParseResult(source, builder.Build(tree), tree, [], builder.Origins);
    }

    public static Ast.Program Parse(string source)
    {
        var r = ParseSource(source);
        return r.Program ?? throw new SiplSyntaxException(r.Diagnostics[0]);
    }

    public static ParseNode ParseTree(string source)
    {
        var r = ParseSource(source);
        return r.Tree ?? throw new SiplSyntaxException(r.Diagnostics[0]);
    }

    private sealed class ParseFailure : Exception;

    private Token Peek => _tokens[_pos];

    private bool Check(TokenKind kind)
    {
        if (_pos > _farPos)
        {
            _farPos = _pos;
            _farExpected.Clear();
        }

        if (_pos == _farPos)
        {
            _farExpected.Add(kind);
        }

        return Peek.Kind == kind;
    }

    private ParseNode Leaf()
    {
        var t = _tokens[_pos++];
        return new ParseNode(SymbolOf(t.Kind), _index.Span(t.Start, t.End), t.Text);
    }

    private ParseNode Expect(TokenKind kind) => Check(kind) ? Leaf() : throw new ParseFailure();

    private ParseNode Node(string symbol, params ParseNode[] children) =>
        new(symbol, _index.Span(children[0].Span.Start, children[^1].Span.End), children);

    private static string SymbolOf(TokenKind kind) => kind switch
    {
        TokenKind.MulOp => "MUL_OP",
        TokenKind.RelOp => "REL_OP",
        _ => kind.ToString().ToUpperInvariant(),
    };

    private ParseNode ProgramRule()
    {
        var seq = Seq();
        return Check(TokenKind.Eof) ? Node("program", seq) : throw new ParseFailure();
    }

    private ParseNode Seq()
    {
        var children = new List<ParseNode> { Statement() };
        while (Check(TokenKind.Semi))
        {
            children.Add(Leaf());
            children.Add(Statement());
        }

        return Node("seq", [.. children]);
    }

    private ParseNode Statement()
    {
        ParseNode inner;
        if (Check(TokenKind.Name))
        {
            inner = Node("assign", Leaf(), Expect(TokenKind.Assign), Expr());
        }
        else if (Check(TokenKind.If))
        {
            inner = Node("if_stmt", Leaf(), Cond(), Expect(TokenKind.Then), Statement(), Expect(TokenKind.Else), Statement());
        }
        else if (Check(TokenKind.While))
        {
            inner = Node("while_stmt", Leaf(), Cond(), Expect(TokenKind.Do), Statement());
        }
        else if (Check(TokenKind.Begin))
        {
            inner = Node("block", Leaf(), Seq(), Expect(TokenKind.End));
        }
        else if (Check(TokenKind.Skip))
        {
            inner = Node("skip_stmt", Leaf());
        }
        else
        {
            throw new ParseFailure();
        }

        return Node("statement", inner);
    }

    private ParseNode Expr()
    {
        var children = new List<ParseNode> { Term() };
        while (Check(TokenKind.Plus) || Check(TokenKind.Minus))
        {
            children.Add(Leaf());
            children.Add(Term());
        }

        return Node("expr", [.. children]);
    }

    private ParseNode Term()
    {
        var children = new List<ParseNode> { Factor() };
        while (Check(TokenKind.MulOp))
        {
            children.Add(Leaf());
            children.Add(Factor());
        }

        return Node("term", [.. children]);
    }

    private ParseNode Factor()
    {
        if (Check(TokenKind.Number) || Check(TokenKind.Name))
        {
            return Node("factor", Leaf());
        }

        if (Check(TokenKind.LPar))
        {
            return Node("factor", Leaf(), Expr(), Expect(TokenKind.RPar));
        }

        if (Check(TokenKind.Minus))
        {
            return Node("factor", Leaf(), Factor());
        }

        throw new ParseFailure();
    }

    private ParseNode Cond()
    {
        var children = new List<ParseNode> { Conj() };
        while (Check(TokenKind.Or))
        {
            children.Add(Leaf());
            children.Add(Conj());
        }

        return Node("cond", [.. children]);
    }

    private ParseNode Conj()
    {
        var children = new List<ParseNode> { Literal() };
        while (Check(TokenKind.And))
        {
            children.Add(Leaf());
            children.Add(Literal());
        }

        return Node("conj", [.. children]);
    }

    private ParseNode Literal()
    {
        if (Check(TokenKind.Not))
        {
            return Node("literal", Leaf(), Literal());
        }

        if (Check(TokenKind.True) || Check(TokenKind.False))
        {
            return Node("literal", Leaf());
        }

        if (Peek.Kind == TokenKind.LPar)
        {
            var save = _pos;
            try
            {
                var node = Node("literal", Leaf(), Cond(), Expect(TokenKind.RPar));
                if (Peek.Kind is not (TokenKind.RelOp or TokenKind.Plus or TokenKind.Minus or TokenKind.MulOp))
                {
                    return node;
                }
            }
            catch (ParseFailure)
            {
            }

            _pos = save;
        }

        return Node("literal", Expr(), Expect(TokenKind.RelOp), Expr());
    }

    private static string Describe(TokenKind kind) => kind switch
    {
        TokenKind.Name => "ім'я змінної",
        TokenKind.Number => "число",
        TokenKind.Eof => "кінець програми",
        TokenKind.RelOp => "порівняння (<, <=, >, >=, =, <>)",
        TokenKind.MulOp => "'*', '/', '%'",
        TokenKind.Assign => "':='",
        TokenKind.Semi => "';'",
        TokenKind.LPar => "'('",
        TokenKind.RPar => "')'",
        TokenKind.Plus => "'+'",
        TokenKind.Minus => "'-'",
        _ => "'" + kind.ToString().ToLowerInvariant() + "'",
    };

    private Diagnostic BuildDiagnostic()
    {
        var tok = _tokens[_farPos];
        var expected = _farExpected.Select(Describe).Distinct().Order(StringComparer.Ordinal).ToList();
        var before = _source[..tok.Start].TrimEnd();

        if (tok.Kind == TokenKind.Error)
        {
            var hint = tok.Text == ":" ? "присвоювання записується як ':='" : null;
            return new Diagnostic(Codes.UnexpectedChar, $"неочікуваний символ '{tok.Text}'",
                _index.Span(tok.Start, tok.End), hint, expected);
        }

        if (tok.Kind == TokenKind.Eof)
        {
            if (_tokens.Count == 1)
            {
                return new Diagnostic(Codes.EmptyProgram, "програма порожня", _index.Span(0, 0),
                    "найменша програма SIPL — 'skip'");
            }

            string? hint = null;
            if (_farExpected.Contains(TokenKind.Else))
            {
                hint = "у SIPL if обов'язково має else (якщо нічого не треба: else skip)";
            }
            else if (before.EndsWith(';'))
            {
                hint = "';' розділяє оператори, у кінці програми вона не потрібна";
            }

            var end = before.Length;
            return new Diagnostic(Codes.UnexpectedEof, "неочікуваний кінець програми", _index.Span(end, end), hint, expected);
        }

        string? h = null;
        if (tok.Text == "=" && _farExpected.Contains(TokenKind.Assign))
        {
            h = "присвоювання записується як ':=', а '=' — це порівняння";
        }
        else if (tok.Kind is TokenKind.End or TokenKind.Else && before.EndsWith(';'))
        {
            h = $"';' розділяє оператори, тому перед '{tok.Text}' її не ставлять";
        }
        else if (tok.Kind is TokenKind.Semi or TokenKind.End && _farExpected.Contains(TokenKind.Else))
        {
            h = "у SIPL if обов'язково має else (якщо нічого не треба: else skip)";
        }
        else if (_farExpected.Contains(TokenKind.Semi) && tok.Kind is TokenKind.Name or TokenKind.If or TokenKind.While or TokenKind.Begin or TokenKind.Skip)
        {
            h = "мабуть, пропущено ';' між операторами";
        }
        else if (Lexer.Keywords.ContainsKey(tok.Text) && _farExpected.Contains(TokenKind.Name))
        {
            h = $"'{tok.Text}' — ключове слово, його не можна брати за ім'я змінної";
        }

        return new Diagnostic(Codes.UnexpectedToken,
            $"неочікувана лексема '{tok.Text}'; очікувалось: {string.Join(", ", expected)}",
            _index.Span(tok.Start, tok.End), h, expected);
    }
}
