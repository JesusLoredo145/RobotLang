using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RobotLang.Language
{
    public sealed class ExprEvaluator
    {
        private readonly RuntimeState _state;

        public ExprEvaluator(RuntimeState state) => _state = state;

        public Value Eval(string expr, int line, int col, string? src)
        {
            var tokens = Tokenize(expr, line, col, src);
            var rpn = ToRpn(tokens, line, col, src);
            return EvalRpn(rpn, line, col, src);
        }

        private enum TKind { Int, String, Ident, Op, LParen, RParen, Comma, Func }
        private readonly record struct Tok(TKind Kind, string Text);

        private static List<Tok> Tokenize(string expr, int line, int col, string? src)
        {
            var t = new List<Tok>();
            int i = 0;

            while (i < expr.Length)
            {
                char c = expr[i];

                if (char.IsWhiteSpace(c)) { i++; continue; }

                // string literal using double quotes
                if (c == '"')
                {
                    int j = i + 1;
                    var sb = new StringBuilder();
                    while (j < expr.Length && expr[j] != '"')
                    {
                        sb.Append(expr[j]);
                        j++;
                    }
                    if (j >= expr.Length) throw new LangError(line, col + i, "Cadena sin comillas de cierre.", src);
                    t.Add(new Tok(TKind.String, sb.ToString()));
                    i = j + 1;
                    continue;
                }

                if (char.IsDigit(c))
                {
                    int j = i;
                    while (j < expr.Length && char.IsDigit(expr[j])) j++;
                    t.Add(new Tok(TKind.Int, expr.Substring(i, j - i)));
                    i = j;
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int j = i;
                    while (j < expr.Length && (char.IsLetterOrDigit(expr[j]) || expr[j] == '_' ||
                                              expr[j] == 'Ñ' || expr[j] == 'ñ' ||
                                              expr[j] == 'Á' || expr[j] == 'É' || expr[j] == 'Í' || expr[j] == 'Ó' || expr[j] == 'Ú' ||
                                              expr[j] == 'á' || expr[j] == 'é' || expr[j] == 'í' || expr[j] == 'ó' || expr[j] == 'ú'))
                        j++;

                    var ident = expr.Substring(i, j - i);
                    var upper = ident.ToUpperInvariant();

                    if (upper == "STR")
                        t.Add(new Tok(TKind.Func, upper));
                    else if (upper is "AND" or "OR" or "NOT")
                        t.Add(new Tok(TKind.Op, upper.ToLowerInvariant()));
                    else if (upper is "TRUE" or "FALSE")
                        t.Add(new Tok(TKind.Ident, upper));
                    else
                        t.Add(new Tok(TKind.Ident, ident));

                    i = j;
                    continue;
                }

                if (c == '(') { t.Add(new Tok(TKind.LParen, "(")); i++; continue; }
                if (c == ')') { t.Add(new Tok(TKind.RParen, ")")); i++; continue; }
                if (c == ',') { t.Add(new Tok(TKind.Comma, ",")); i++; continue; }

                string two = (i + 1 < expr.Length) ? expr.Substring(i, 2) : "";
                if (two is "==" or "!=" or "<=" or ">=")
                {
                    t.Add(new Tok(TKind.Op, two));
                    i += 2;
                    continue;
                }
                if (c is '+' or '-' or '*' or '/' or '<' or '>')
                {
                    t.Add(new Tok(TKind.Op, c.ToString()));
                    i++;
                    continue;
                }

                throw new LangError(line, col + i, $"Carácter inválido en expresión: '{c}'", src);
            }

            return FixUnarySigns(t);
        }

        private static List<Tok> FixUnarySigns(List<Tok> tokens)
        {
            var result = new List<Tok>();
            for (int i = 0; i < tokens.Count; i++)
            {
                var tok = tokens[i];
                if (tok.Kind == TKind.Op && (tok.Text == "-" || tok.Text == "+"))
                {
                    bool isUnary = (i == 0) || (tokens[i - 1].Kind is TKind.Op or TKind.LParen or TKind.Comma);
                    if (isUnary)
                        result.Add(new Tok(TKind.Int, "0"));
                }
                result.Add(tok);
            }
            return result;
        }

        private static int Prec(string op) => op switch
        {
            "not" => 5,
            "*" or "/" => 4,
            "+" or "-" => 3,
            "<" or "<=" or ">" or ">=" => 2,
            "==" or "!=" => 2,
            "and" => 1,
            "or" => 0,
            _ => -1
        };

        private static bool RightAssoc(string op) => op == "not";

        private static List<Tok> ToRpn(List<Tok> tokens, int line, int col, string? src)
        {
            var output = new List<Tok>();
            var stack = new Stack<Tok>();

            for (int i = 0; i < tokens.Count; i++)
            {
                var tok = tokens[i];

                if (tok.Kind is TKind.Int or TKind.String or TKind.Ident)
                {
                    output.Add(tok);
                    continue;
                }

                if (tok.Kind == TKind.Func)
                {
                    stack.Push(tok);
                    continue;
                }

                if (tok.Kind == TKind.Comma)
                {
                    while (stack.Count > 0 && stack.Peek().Kind != TKind.LParen)
                        output.Add(stack.Pop());
                    continue;
                }

                if (tok.Kind == TKind.Op)
                {
                    while (stack.Count > 0 && stack.Peek().Kind == TKind.Op)
                    {
                        var top = stack.Peek().Text;
                        var p1 = Prec(tok.Text);
                        var p2 = Prec(top);
                        if (p2 > p1 || (p2 == p1 && !RightAssoc(tok.Text)))
                            output.Add(stack.Pop());
                        else
                            break;
                    }
                    stack.Push(tok);
                    continue;
                }

                if (tok.Kind == TKind.LParen)
                {
                    stack.Push(tok);
                    continue;
                }

                if (tok.Kind == TKind.RParen)
                {
                    while (stack.Count > 0 && stack.Peek().Kind != TKind.LParen)
                        output.Add(stack.Pop());

                    if (stack.Count == 0) throw new LangError(line, col, "Paréntesis desbalanceados.", src);
                    stack.Pop();

                    if (stack.Count > 0 && stack.Peek().Kind == TKind.Func)
                        output.Add(stack.Pop());

                    continue;
                }
            }

            while (stack.Count > 0)
            {
                if (stack.Peek().Kind is TKind.LParen or TKind.RParen)
                    throw new LangError(line, col, "Paréntesis desbalanceados.", src);
                output.Add(stack.Pop());
            }

            return output;
        }

        private Value EvalRpn(List<Tok> rpn, int line, int col, string? src)
        {
            var st = new Stack<Value>();

            foreach (var tok in rpn)
            {
                if (tok.Kind == TKind.Int)
                {
                    st.Push(Value.FromInt(int.Parse(tok.Text, CultureInfo.InvariantCulture)));
                    continue;
                }

                if (tok.Kind == TKind.String)
                {
                    st.Push(Value.FromString(tok.Text));
                    continue;
                }

                if (tok.Kind == TKind.Ident)
                {
                    var upper = tok.Text.ToUpperInvariant();
                    if (upper == "TRUE") { st.Push(Value.FromBool(true)); continue; }
                    if (upper == "FALSE") { st.Push(Value.FromBool(false)); continue; }

                    if (_state.Variables.TryGetValue(tok.Text, out var v))
                    {
                        st.Push(v);
                        continue;
                    }

                    throw new LangError(line, col, $"Variable no declarada: '{tok.Text}'", src);
                }

                if (tok.Kind == TKind.Func)
                {
                    if (tok.Text == "STR")
                    {
                        if (st.Count < 1) throw new LangError(line, col, "STR() requiere 1 argumento.", src);
                        var a = st.Pop();
                        st.Push(Value.FromString(a.AsString()));
                        continue;
                    }

                    throw new LangError(line, col, $"Función desconocida: {tok.Text}", src);
                }

                if (tok.Kind == TKind.Op)
                {
                    var op = tok.Text;

                    if (op == "not")
                    {
                        if (st.Count < 1) throw new LangError(line, col, "Operador 'not' requiere 1 operando.", src);
                        var a = st.Pop();
                        st.Push(Value.FromBool(!a.AsBool(line, col, src)));
                        continue;
                    }

                    if (st.Count < 2) throw new LangError(line, col, $"Operador '{op}' requiere 2 operandos.", src);
                    var b = st.Pop();
                    var a2 = st.Pop();

                    st.Push(ApplyBinary(op, a2, b, line, col, src));
                    continue;
                }

                throw new LangError(line, col, "Token inválido en evaluación.", src);
            }

            if (st.Count != 1) throw new LangError(line, col, "Expresión inválida (sobran operandos).", src);
            return st.Pop();
        }

        private static Value ApplyBinary(string op, Value a, Value b, int line, int col, string? src)
        {
            if (op == "+")
            {
                if (a.Kind == Value.KindType.String || b.Kind == Value.KindType.String)
                    return Value.FromString(a.AsString() + b.AsString());

                return Value.FromInt(a.AsInt(line, col, src) + b.AsInt(line, col, src));
            }

            if (op == "-") return Value.FromInt(a.AsInt(line, col, src) - b.AsInt(line, col, src));
            if (op == "*") return Value.FromInt(a.AsInt(line, col, src) * b.AsInt(line, col, src));
            if (op == "/")
            {
                var denom = b.AsInt(line, col, src);
                if (denom == 0) throw new LangError(line, col, "División entre cero.", src);
                return Value.FromInt(a.AsInt(line, col, src) / denom);
            }

            if (op is "==" or "!=")
            {
                bool eq = a.AsString() == b.AsString();
                return Value.FromBool(op == "==" ? eq : !eq);
            }

            if (op is "<" or "<=" or ">" or ">=")
            {
                if (a.Kind == Value.KindType.Int && b.Kind == Value.KindType.Int)
                {
                    int x = a.IntValue, y = b.IntValue;
                    return Value.FromBool(op switch
                    {
                        "<" => x < y,
                        "<=" => x <= y,
                        ">" => x > y,
                        ">=" => x >= y,
                        _ => false
                    });
                }

                int cmp = string.CompareOrdinal(a.AsString(), b.AsString());
                return Value.FromBool(op switch
                {
                    "<" => cmp < 0,
                    "<=" => cmp <= 0,
                    ">" => cmp > 0,
                    ">=" => cmp >= 0,
                    _ => false
                });
            }

            if (op == "and") return Value.FromBool(a.AsBool(line, col, src) && b.AsBool(line, col, src));
            if (op == "or") return Value.FromBool(a.AsBool(line, col, src) || b.AsBool(line, col, src));

            throw new LangError(line, col, $"Operador no soportado: '{op}'", src);
        }
    }
}
