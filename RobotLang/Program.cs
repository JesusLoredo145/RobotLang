using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RobotLang
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            var path = args.Length > 0 ? args[0] : "programa.txt";
            if (!File.Exists(path))
            {
                Console.WriteLine($"No existe el archivo: {path}");
                Console.WriteLine("Uso: dotnet run -- programa.txt");
                return 1;
            }

            var lines = File.ReadAllLines(path);

            // Config del brazo: 4 articulaciones por defecto (extensible)
            var robotConfig = RobotConfig.Default4Joints();
            var robot = new MockRobotTransport(); // luego cambias a Serial/WiFi

            var interpreter = new Interpreter(robotConfig, robot);

            try
            {
                interpreter.Run(lines);
                Console.WriteLine("\n✅ Programa finalizado.");
                return 0;
            }
            catch (LangError e)
            {
                Console.WriteLine($"\n❌ Error en línea {e.Line}: {e.Message}");
                if (!string.IsNullOrWhiteSpace(e.SourceLine))
                {
                    Console.WriteLine($"   {e.SourceLine}");
                    if (e.Column > 0)
                        Console.WriteLine($"   {new string(' ', Math.Max(0, e.Column - 1))}^");
                }
                return 2;
            }
        }
    }

    // =========================
    // Errores del lenguaje
    // =========================
    public class LangError : Exception
    {
        public int Line { get; }
        public int Column { get; }
        public string? SourceLine { get; }

        public LangError(int line, int column, string message, string? sourceLine = null) : base(message)
        {
            Line = line;
            Column = column;
            SourceLine = sourceLine;
        }
    }

    // =========================
    // Runtime State
    // =========================
    public sealed class RuntimeState
    {
        public Dictionary<string, Value> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

        // Estado lógico del brazo (grados o unidades arbitrarias)
        public Dictionary<string, int> JointPositions { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // =========================
    // Value (int/string/bool)
    // =========================
    public readonly struct Value
    {
        public enum KindType { Int, String, Bool }

        public KindType Kind { get; }
        public int IntValue { get; }
        public string StringValue { get; }
        public bool BoolValue { get; }

        private Value(KindType kind, int i, string s, bool b)
        {
            Kind = kind;
            IntValue = i;
            StringValue = s;
            BoolValue = b;
        }

        public static Value FromInt(int v) => new(KindType.Int, v, "", false);
        public static Value FromString(string v) => new(KindType.String, 0, v ?? "", false);
        public static Value FromBool(bool v) => new(KindType.Bool, 0, "", v);

        public override string ToString()
        {
            return Kind switch
            {
                KindType.Int => IntValue.ToString(CultureInfo.InvariantCulture),
                KindType.String => StringValue,
                KindType.Bool => BoolValue ? "verdadero" : "falso",
                _ => ""
            };
        }

        public int AsInt(int line, int col, string? src)
        {
            if (Kind != KindType.Int) throw new LangError(line, col, "Se esperaba un entero.", src);
            return IntValue;
        }

        public bool AsBool(int line, int col, string? src)
        {
            if (Kind != KindType.Bool) throw new LangError(line, col, "Se esperaba un booleano.", src);
            return BoolValue;
        }

        public string AsString()
        {
            return Kind switch
            {
                KindType.String => StringValue,
                KindType.Int => IntValue.ToString(CultureInfo.InvariantCulture),
                KindType.Bool => BoolValue ? "true" : "false",
                _ => ""
            };
        }
    }

    // =========================
    // Config + Safety del brazo
    // =========================
    public sealed class JointSpec
    {
        public string Name { get; init; } = "";
        public int Min { get; init; }
        public int Max { get; init; }
        public int Home { get; init; }
    }

    public sealed class RobotConfig
    {
        public Dictionary<string, JointSpec> Joints { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static RobotConfig Default4Joints()
        {
            var c = new RobotConfig();
            c.Joints["BASE"] = new JointSpec { Name = "BASE", Min = 0, Max = 180, Home = 90 };
            c.Joints["HOMBRO"] = new JointSpec { Name = "HOMBRO", Min = 10, Max = 170, Home = 90 };
            c.Joints["CODO"] = new JointSpec { Name = "CODO", Min = 0, Max = 180, Home = 90 };
            c.Joints["MUÑECA"] = new JointSpec { Name = "MUÑECA", Min = 0, Max = 180, Home = 90 };
            // Si luego quieres pinza como articulación:
            // c.Joints["PINZA"] = new JointSpec { Name="PINZA", Min=0, Max=100, Home=0 };
            return c;
        }
    }

    public interface IRobotTransport
    {
        void MoveJoint(string joint, int target);
        void HomeAll(Dictionary<string, int> homePositions);
        void Gripper(string action); // "ABRIR"/"CERRAR"
        void WaitMs(int ms);
    }

    public sealed class MockRobotTransport : IRobotTransport
    {
        public void MoveJoint(string joint, int target) =>
            Console.WriteLine($"[ROBOT] MOVE {joint} => {target}");

        public void HomeAll(Dictionary<string, int> homePositions)
        {
            Console.WriteLine("[ROBOT] HOME");
            foreach (var kv in homePositions)
                Console.WriteLine($"        {kv.Key} = {kv.Value}");
        }

        public void Gripper(string action) =>
            Console.WriteLine($"[ROBOT] GRIP {action}");

        public void WaitMs(int ms) =>
            Console.WriteLine($"[ROBOT] WAIT {ms}ms");
    }

    // =========================
    // Intérprete principal
    // =========================
    public sealed class Interpreter
    {
        private readonly RobotConfig _robotConfig;
        private readonly IRobotTransport _robot;
        private readonly RuntimeState _state = new();
        private readonly ExprEvaluator _eval;

        public Interpreter(RobotConfig robotConfig, IRobotTransport robot)
        {
            _robotConfig = robotConfig;
            _robot = robot;
            _eval = new ExprEvaluator(_state);
            InitRobotState();
        }

        private void InitRobotState()
        {
            foreach (var j in _robotConfig.Joints.Values)
                _state.JointPositions[j.Name] = j.Home;
        }

        public void Run(string[] lines)
        {
            InterpretBlock(lines, 0, lines.Length);
        }

        // Interpreta un bloque [start, end)
        private void InterpretBlock(string[] lines, int start, int end)
        {
            int i = start;
            while (i < end)
            {
                var raw = lines[i];
                var lineNo = i + 1;
                var line = raw.Trim();

                // vacías / comentarios
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    i++;
                    continue;
                }

                // ===== Declaración: entero|cadena nombre = expr;
                {
                    var m = Regex.Match(line, @"^(entero|cadena)\s+([A-Za-z_]\w*)\s*=\s*(.+);$");
                    if (m.Success)
                    {
                        var tipo = m.Groups[1].Value;
                        var nombre = m.Groups[2].Value;
                        var expr = m.Groups[3].Value.Trim();

                        if (tipo.Equals("cadena", StringComparison.OrdinalIgnoreCase))
                        {
                            var v = _eval.Eval(expr, lineNo, 1, raw);
                            _state.Variables[nombre] = Value.FromString(v.AsString());
                        }
                        else
                        {
                            var v = _eval.Eval(expr, lineNo, 1, raw);
                            _state.Variables[nombre] = Value.FromInt(v.AsInt(lineNo, 1, raw));
                        }

                        i++;
                        continue;
                    }
                }

                // ===== Asignación: nombre = expr; (solo si ya existe)
                {
                    var m = Regex.Match(line, @"^([A-Za-z_]\w*)\s*=\s*(.+);$");
                    if (m.Success && _state.Variables.ContainsKey(m.Groups[1].Value))
                    {
                        var nombre = m.Groups[1].Value;
                        var expr = m.Groups[2].Value.Trim();

                        var current = _state.Variables[nombre];
                        var v = _eval.Eval(expr, lineNo, 1, raw);

                        if (current.Kind == Value.KindType.Int)
                            _state.Variables[nombre] = Value.FromInt(v.AsInt(lineNo, 1, raw));
                        else
                            _state.Variables[nombre] = Value.FromString(v.AsString());

                        i++;
                        continue;
                    }
                }

                // ===== Imprimir(expr);
                {
                    var m = Regex.Match(line, @"^Imprimir\((.+)\);$");
                    if (m.Success)
                    {
                        var expr = m.Groups[1].Value.Trim();
                        var v = _eval.Eval(expr, lineNo, 1, raw);
                        Console.WriteLine(v.AsString());
                        i++;
                        continue;
                    }
                }

                // ===== Pausar();
                if (Regex.IsMatch(line, @"^Pausar\(\);$"))
                {
                    Console.Write("\n[Ejecución pausada] ENTER para continuar...");
                    Console.ReadLine();
                    i++;
                    continue;
                }

                // =========================
                // === COMANDOS DEL BRAZO ===
                // =========================

                // Centrar();
                if (Regex.IsMatch(line, @"^Centrar\(\);$", RegexOptions.IgnoreCase))
                {
                    var homes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var j in _robotConfig.Joints.Values)
                    {
                        _state.JointPositions[j.Name] = j.Home;
                        homes[j.Name] = j.Home;
                    }
                    _robot.HomeAll(homes);
                    i++;
                    continue;
                }

                // Esperar(ms);
                {
                    var m = Regex.Match(line, @"^Esperar\(\s*(.+)\s*\);$", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        var v = _eval.Eval(m.Groups[1].Value, lineNo, 1, raw);
                        var ms = v.AsInt(lineNo, 1, raw);
                        if (ms < 0 || ms > 600000)
                            throw new LangError(lineNo, 1, "Tiempo de espera fuera de rango (0..600000 ms).", raw);

                        _robot.WaitMs(ms);
                        i++;
                        continue;
                    }
                }

                // Pinza(ABRIR|CERRAR);
                {
                    var m = Regex.Match(line, @"^Pinza\(\s*(ABRIR|CERRAR)\s*\);$", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        _robot.Gripper(m.Groups[1].Value.ToUpperInvariant());
                        i++;
                        continue;
                    }
                }

                // Mover(JOINT, delta);
                {
                    var m = Regex.Match(line, @"^Mover\(\s*([A-Za-zÁÉÍÓÚÑ_]\w*)\s*,\s*(.+)\s*\);$", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        var joint = NormalizeJoint(m.Groups[1].Value);
                        var deltaVal = _eval.Eval(m.Groups[2].Value, lineNo, 1, raw);
                        var delta = deltaVal.AsInt(lineNo, 1, raw);

                        ApplyMove(lineNo, raw, joint, delta);
                        i++;
                        continue;
                    }
                }

                // =========================
                // === BLOQUES: Si / Mientras
                // =========================

                // Si cond Entonces
                if (line.StartsWith("Si", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains("Entonces", StringComparison.OrdinalIgnoreCase))
                {
                    var cond = ExtractBetween(line, "Si", "Entonces", lineNo, raw);
                    var (blockStart, blockEndExclusive, closeIdx) = CollectBlock(lines, i + 1, "Si", "FinSi");

                    var condVal = _eval.Eval(cond, lineNo, 1, raw).AsBool(lineNo, 1, raw);
                    if (condVal)
                        InterpretBlock(lines, blockStart, blockEndExclusive);

                    i = closeIdx + 1; // saltar FinSi
                    continue;
                }

                // Mientras cond Hacer
                if (line.StartsWith("Mientras", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains("Hacer", StringComparison.OrdinalIgnoreCase))
                {
                    var cond = ExtractBetween(line, "Mientras", "Hacer", lineNo, raw);
                    var (blockStart, blockEndExclusive, closeIdx) = CollectBlock(lines, i + 1, "Mientras", "FinMientras");

                    int safety = 0;
                    while (_eval.Eval(cond, lineNo, 1, raw).AsBool(lineNo, 1, raw))
                    {
                        InterpretBlock(lines, blockStart, blockEndExclusive);
                        safety++;
                        if (safety > 10000)
                            throw new LangError(lineNo, 1, "Posible bucle infinito (límite 10000 iteraciones).", raw);
                    }

                    i = closeIdx + 1; // saltar FinMientras
                    continue;
                }

                // Si llegó aquí: no reconocimos la línea
                throw new LangError(lineNo, 1, $"Línea no reconocida / Sintaxis inválida: {line}", raw);
            }
        }

        private void ApplyMove(int lineNo, string raw, string joint, int delta)
        {
            if (!_robotConfig.Joints.TryGetValue(joint, out var spec))
                throw new LangError(lineNo, 1, $"Articulación desconocida: '{joint}'.", raw);

            var current = _state.JointPositions.TryGetValue(joint, out var cur) ? cur : spec.Home;
            var target = current + delta;

            if (target < spec.Min || target > spec.Max)
                throw new LangError(lineNo, 1,
                    $"Riesgo físico: {joint} fuera de rango. Actual={current}, Δ={delta}, Target={target}, permitido=[{spec.Min}..{spec.Max}]",
                    raw);

            _state.JointPositions[joint] = target;
            _robot.MoveJoint(joint, target);
        }

        private static string NormalizeJoint(string s) => s.Trim().ToUpperInvariant();

        private static string ExtractBetween(string line, string left, string right, int lineNo, string raw)
        {
            var idxL = line.IndexOf(left, StringComparison.OrdinalIgnoreCase);
            var idxR = line.LastIndexOf(right, StringComparison.OrdinalIgnoreCase);
            if (idxL < 0 || idxR < 0 || idxR <= idxL)
                throw new LangError(lineNo, 1, $"No se pudo extraer condición entre {left} y {right}.", raw);

            var inside = line.Substring(idxL + left.Length, idxR - (idxL + left.Length));
            return inside.Trim();
        }

        private static (int blockStart, int blockEndExclusive, int closeIndex) CollectBlock(
            string[] lines,
            int start,
            string openKeyword,
            string closeKeyword)
        {
            int i = start;
            int level = 1;
            int blockStart = start;

            while (i < lines.Length)
            {
                var l = lines[i].Trim();

                bool isOpen =
                    openKeyword.Equals("Si", StringComparison.OrdinalIgnoreCase)
                        ? (l.StartsWith("Si", StringComparison.OrdinalIgnoreCase) &&
                           l.Contains("Entonces", StringComparison.OrdinalIgnoreCase))
                        : (l.StartsWith("Mientras", StringComparison.OrdinalIgnoreCase) &&
                           l.Contains("Hacer", StringComparison.OrdinalIgnoreCase));

                if (isOpen) level++;

                if (l.Equals(closeKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    level--;
                    if (level == 0)
                    {
                        // bloque: [blockStart, i) sin incluir el cierre
                        return (blockStart, i, i);
                    }
                }

                i++;
            }

            throw new LangError(start, 1, $"Bloque sin cierre: falta '{closeKeyword}'.", lines[Math.Max(0, start - 1)]);
        }
    }

    // =========================
    // Evaluador de expresiones (sin eval)
    // =========================
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

                // string "..."
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

                // number
                if (char.IsDigit(c))
                {
                    int j = i;
                    while (j < expr.Length && char.IsDigit(expr[j])) j++;
                    t.Add(new Tok(TKind.Int, expr.Substring(i, j - i)));
                    i = j;
                    continue;
                }

                // ident / keywords
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
                        t.Add(new Tok(TKind.Ident, upper)); // literal bool
                    else
                        t.Add(new Tok(TKind.Ident, ident));

                    i = j;
                    continue;
                }

                // punctuation
                if (c == '(') { t.Add(new Tok(TKind.LParen, "(")); i++; continue; }
                if (c == ')') { t.Add(new Tok(TKind.RParen, ")")); i++; continue; }
                if (c == ',') { t.Add(new Tok(TKind.Comma, ",")); i++; continue; }

                // operators (2-char first)
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

            t = FixUnarySigns(t);
            return t;
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
                    stack.Pop(); // '('

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
                else
                {
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
            }

            if (op == "and") return Value.FromBool(a.AsBool(line, col, src) && b.AsBool(line, col, src));
            if (op == "or") return Value.FromBool(a.AsBool(line, col, src) || b.AsBool(line, col, src));

            throw new LangError(line, col, $"Operador no soportado: '{op}'", src);
        }
    }
}
