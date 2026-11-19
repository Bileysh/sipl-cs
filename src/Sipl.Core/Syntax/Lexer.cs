namespace Sipl.Syntax;

public enum TokenKind
{
    Name, Number, Assign, Semi, LPar, RPar,
    If, Then, Else, While, Do, Begin, End, Skip, True, False,
    Or, And, Not, Plus, Minus, MulOp, RelOp,
    Eof,
    Error,
}

public readonly record struct Token(TokenKind Kind, string Text, int Start, int End);

public static class Lexer
{
    public static readonly IReadOnlyDictionary<string, TokenKind> Keywords = new Dictionary<string, TokenKind>
    {
        ["if"] = TokenKind.If,
        ["then"] = TokenKind.Then,
        ["else"] = TokenKind.Else,
        ["while"] = TokenKind.While,
        ["do"] = TokenKind.Do,
        ["begin"] = TokenKind.Begin,
        ["end"] = TokenKind.End,
        ["skip"] = TokenKind.Skip,
        ["true"] = TokenKind.True,
        ["false"] = TokenKind.False,
        ["or"] = TokenKind.Or,
        ["and"] = TokenKind.And,
        ["not"] = TokenKind.Not,
    };

    private static readonly (string Text, TokenKind Kind)[] Operators =
    [
        (":=", TokenKind.Assign),
        ("<=", TokenKind.RelOp),
        (">=", TokenKind.RelOp),
        ("<>", TokenKind.RelOp),
        ("!=", TokenKind.RelOp),
        ("||", TokenKind.Or),
        ("&&", TokenKind.And),
        (";", TokenKind.Semi),
        ("(", TokenKind.LPar),
        (")", TokenKind.RPar),
        ("+", TokenKind.Plus),
        ("-", TokenKind.Minus),
        ("*", TokenKind.MulOp),
        ("/", TokenKind.MulOp),
        ("%", TokenKind.MulOp),
        ("÷", TokenKind.MulOp),
        ("<", TokenKind.RelOp),
        (">", TokenKind.RelOp),
        ("=", TokenKind.RelOp),
        ("≤", TokenKind.RelOp),
        ("≥", TokenKind.RelOp),
        ("≠", TokenKind.RelOp),
        ("∨", TokenKind.Or),
        ("∧", TokenKind.And),
        ("¬", TokenKind.Not),
        ("!", TokenKind.Not),
    ];

    public static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (c is ' ' or '\t' or '\n' or '\r')
            {
                i++;
            }
            else if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n')
                {
                    i++;
                }
            }
            else if (c == '{' && source.IndexOf('}', i) is var close and >= 0)
            {
                i = close + 1;
            }
            else if (char.IsAsciiLetter(c))
            {
                var start = i;
                while (i < source.Length && (char.IsAsciiLetterOrDigit(source[i]) || source[i] == '_'))
                {
                    i++;
                }

                var word = source[start..i];
                tokens.Add(new Token(Keywords.GetValueOrDefault(word, TokenKind.Name), word, start, i));
            }
            else if (char.IsAsciiDigit(c))
            {
                var start = i;
                while (i < source.Length && char.IsAsciiDigit(source[i]))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Number, source[start..i], start, i));
            }
            else if (Operators.FirstOrDefault(op => source.AsSpan(i).StartsWith(op.Text)) is { Text: not null } op)
            {
                tokens.Add(new Token(op.Kind, op.Text, i, i + op.Text.Length));
                i += op.Text.Length;
            }
            else
            {
                tokens.Add(new Token(TokenKind.Error, source[i].ToString(), i, i + 1));
                i++;
            }
        }

        tokens.Add(new Token(TokenKind.Eof, "", source.Length, source.Length));
        return tokens;
    }
}
