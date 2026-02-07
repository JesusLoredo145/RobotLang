using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;

namespace RobotLang.Transports
{
    public sealed class ArduinoSerialTransport : IRobotTransport, IDisposable
    {
        private readonly SerialPort _sp;
        private readonly int _ackTimeoutMs;

        public ArduinoSerialTransport(string portName, int baudRate = 115200, int ackTimeoutMs = 2000)
        {
            _ackTimeoutMs = ackTimeoutMs;

            _sp = new SerialPort(portName, baudRate)
            {
                NewLine = "\n",
                ReadTimeout = ackTimeoutMs,
                WriteTimeout = 1000,
                DtrEnable = true,
                RtsEnable = true
            };

            _sp.Open();

            // many boards reset on serial open, wait and clear buffers
            Thread.Sleep(1200);
            _sp.DiscardInBuffer();
            _sp.DiscardOutBuffer();
        }

        public void Dispose()
        {
            try { if (_sp.IsOpen) _sp.Close(); } catch { }
        }

        public void HomeAll(Dictionary<string, int> homePositions)
        {
            SendExpectOk("HOME");
        }

        public void MoveJoint(string joint, int target)
        {
            joint = NormalizeToAsciiUpper(joint);
            SendExpectOk($"MOVE {joint} {target}");
        }

        public void WaitMs(int ms)
        {
            SendExpectOk($"WAIT {ms}");
            Thread.Sleep(ms);
        }

        public void Gripper(string action)
        {
            throw new NotSupportedException("Pinza no implementada en fase 3 articulaciones.");
        }

        private void SendExpectOk(string cmd)
        {
            _sp.WriteLine(cmd);

            var start = Environment.TickCount;
            while (true)
            {
                if (Environment.TickCount - start > _ackTimeoutMs)
                    throw new Exception($"Arduino no respondió a tiempo para: {cmd}");

                string line;
                try
                {
                    line = _sp.ReadLine()?.Trim() ?? "";
                }
                catch (TimeoutException)
                {
                    continue;
                }

                if (line.Length == 0) continue;

                if (line.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                    return;

                if (line.StartsWith("ERR", StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"Arduino respondió error: {line} (comando: {cmd})");
            }
        }

        private static string NormalizeToAsciiUpper(string s)
        {
            return (s ?? "")
                .Trim()
                .ToUpperInvariant()
                .Replace("Ñ", "N")
                .Replace("Á", "A")
                .Replace("É", "E")
                .Replace("Í", "I")
                .Replace("Ó", "O")
                .Replace("Ú", "U");
        }
    }
}
