using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RobotLang.Language;
using RobotLang.Transports;

namespace RobotLang.Robot
{
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

        private void InterpretBlock(string[] lines, int start, int end)
        {
            int i = start;
            while (i < end)
            {
                var raw = lines[i];
                var lineNo = i + 1;
                var line = raw.Trim();

                if (line.Length == 0 || line.StartsWith("#"))
                {
                    i++;
                    continue;
                }

                {
                    var m = Regex.Match(line, @"^(entero|cadena)\s+([A-Za-z_]\w*)\s*=\s*(.+);$");
                    if (m.Success)
                    {
                        var tipo = m.Groups[1].Value;
                        var nombre = m.Groups[2].Value;
                        var expr = m.Groups[3].Value.Trim();

                        var v = _eval.Eval(expr, lineNo, 1, raw);

                        if (tipo.Equals("cadena", StringComparison.OrdinalIgnoreCase))
                            _state.Variables[nombre] = Value.FromString(v.AsString());
                        else
                            _state.Variables[nombre] = Value.FromInt(v.AsInt(lineNo, 1, raw));

                        i++;
                        continue;
                    }
                }

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

                if (Regex.IsMatch(line, @"^Pausar\(\);$"))
                {
                    Console.Write("\n[Ejecución pausada] ENTER para continuar...");
                    Console.ReadLine();
                    i++;
                    continue;
                }

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

                {
                    var m = Regex.Match(line, @"^Pinza\(\s*(ABRIR|CERRAR)\s*\);$", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        _robot.Gripper(m.Groups[1].Value.ToUpperInvariant());
                        i++;
                        continue;
                    }
                }

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

                if (line.StartsWith("Si", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains("Entonces", StringComparison.OrdinalIgnoreCase))
                {
                    var cond = ExtractBetween(line, "Si", "Entonces", lineNo, raw);
                    var (blockStart, blockEndExclusive, closeIdx) = CollectBlock(lines, i + 1, "Si", "FinSi");

                    var condVal = _eval.Eval(cond, lineNo, 1, raw).AsBool(lineNo, 1, raw);
                    if (condVal)
                        InterpretBlock(lines, blockStart, blockEndExclusive);

                    i = closeIdx + 1;
                    continue;
                }

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

                    i = closeIdx + 1;
                    continue;
                }

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
                    $"Riesgo físico: {joint} fuera de rango. Actual={current}, ?={delta}, Target={target}, permitido=[{spec.Min}..{spec.Max}]",
                    raw);

            _state.JointPositions[joint] = target;
            _robot.MoveJoint(joint, target);
        }

        private static string NormalizeJoint(string s) => (s ?? "").Trim().ToUpperInvariant();

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
                        return (blockStart, i, i);
                }

                i++;
            }

            throw new LangError(start, 1, $"Bloque sin cierre: falta '{closeKeyword}'.", lines[Math.Max(0, start - 1)]);
        }
    }
}
