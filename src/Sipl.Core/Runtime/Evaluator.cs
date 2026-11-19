using System.Numerics;
using Sipl.Ast;
using Sipl.Diagnostics;

namespace Sipl.Runtime;

public static class Evaluator
{
    public static BigInteger FloorDiv(BigInteger a, BigInteger b)
    {
        var q = BigInteger.DivRem(a, b, out var r);
        return !r.IsZero && (r.Sign < 0) != (b.Sign < 0) ? q - 1 : q;
    }

    public static BigInteger FloorMod(BigInteger a, BigInteger b) => a - b * FloorDiv(a, b);

    public static BigInteger Arith(AExpr node, IReadOnlyDictionary<string, BigInteger> state)
    {
        switch (node)
        {
            case Num n:
                return n.Value;
            case Var v:
                return state.TryGetValue(v.Name, out var value)
                    ? value
                    : throw new SiplRuntimeException(new Diagnostic(Codes.UndefinedVariable,
                        $"змінна '{v.Name}' не має значення", v.Span, $"задайте її: --set {v.Name}=..."));
            case Neg g:
                return -Arith(g.Operand, state);
            case BinOp b:
                var left = Arith(b.Left, state);
                var right = Arith(b.Right, state);
                if (b.Op is "/" or "%" && right.IsZero)
                {
                    throw new SiplRuntimeException(new Diagnostic(Codes.DivisionByZero, "ділення на нуль", b.Span));
                }

                return b.Op switch
                {
                    "+" => left + right,
                    "-" => left - right,
                    "*" => left * right,
                    "/" => FloorDiv(left, right),
                    _ => FloorMod(left, right),
                };
            default:
                throw new ArgumentException(node.Kind);
        }
    }

    public static bool Bool(BExpr node, IReadOnlyDictionary<string, BigInteger> state)
    {
        switch (node)
        {
            case BoolConst c:
                return c.Value;
            case Not n:
                return !Bool(n.Operand, state);
            case BoolOp o:
                var l = Bool(o.Left, state);
                var r = Bool(o.Right, state);
                return o.Op == "and" ? l && r : l || r;
            case Compare c:
                var a = Arith(c.Left, state);
                var b = Arith(c.Right, state);
                return c.Op switch
                {
                    "<" => a < b,
                    "<=" => a <= b,
                    ">" => a > b,
                    ">=" => a >= b,
                    "=" => a == b,
                    _ => a != b,
                };
            default:
                throw new ArgumentException(node.Kind);
        }
    }
}
