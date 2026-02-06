using System;
using System.IO.Ports;
using System.Threading;

namespace RobotLang
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

            // Muchos Arduinos se resetean al abrir serial: espera un poco y limpia buffer
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
            // opcional: mandar también MOVE por cada joint a Home si quieres ser explícito:
            // foreach (var kv in homePositions) SendExpectOk($"MOVE {kv.Key.ToUpperInvariant()} {kv.Value}");
        }

        public void MoveJoint(string joint, int target)
        {
            SendExpectOk($"MOVE {joint.ToUpperInvariant()} {target}");
        }

        public void WaitMs(int ms)
        {
            // Puedes esperar en PC o en Arduino. Recomendación: ambas.
            SendExpectOk($"WAIT {ms}");
            Thread.Sleep(ms);
        }

        public void Gripper(string action)
        {
            // Aún no usas mano; puedes mandar ERR o ignorar.
            // Si no lo vas a usar ahorita, mejor que truene en C#:
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

                // Arduino puede mandar "OK READY" al inicio
                if (line.StartsWith("OK"))
                    return;

                if (line.StartsWith("ERR"))
                    throw new Exception($"Arduino respondió error: {line} (comando: {cmd})");

                // Si manda ruido, seguimos leyendo
            }
        }
    }
}
