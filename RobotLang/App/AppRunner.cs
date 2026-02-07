using System;
using System.IO;
using RobotLang.Language;
using RobotLang.Robot;
using RobotLang.Transports;

namespace RobotLang.App
{
    public sealed class AppRunner
    {
        public int Run(string[] args)
        {
            var options = CommandLineOptions.Parse(args);

            if (!File.Exists(options.ScriptPath))
            {
                Console.WriteLine($"No existe el archivo: {options.ScriptPath}");
                Console.WriteLine("Uso: dotnet run -- programa.txt [el-COM-que-tengas-en-arduino]");
                return 1;
            }

            var lines = File.ReadAllLines(options.ScriptPath);

            var robotConfig = RobotConfig.Default4Joints();

            IRobotTransport robot = options.PortName is null
                ? new MockRobotTransport()
                : new ArduinoSerialTransport(options.PortName, 9600);

            var interpreter = new Interpreter(robotConfig, robot);

            try
            {
                interpreter.Run(lines);
                Console.WriteLine("\nPrograma finalizado.");
                return 0;
            }
            catch (LangError e)
            {
                Console.WriteLine($"\nError en línea {e.Line}: {e.Message}");
                if (!string.IsNullOrWhiteSpace(e.SourceLine))
                {
                    Console.WriteLine($"   {e.SourceLine}");
                    if (e.Column > 0)
                        Console.WriteLine($"   {new string(' ', Math.Max(0, e.Column - 1))}^");
                }
                return 2;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError de comunicación/ejecución: {ex.Message}");
                return 3;
            }
            finally
            {
                if (robot is IDisposable d) d.Dispose();
            }
        }
    }
}
