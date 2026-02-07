using System;

namespace RobotLang.App
{
    public sealed class CommandLineOptions
    {
        public string ScriptPath { get; }
        public string? PortName { get; }

        private CommandLineOptions(string scriptPath, string? portName)
        {
            ScriptPath = scriptPath;
            PortName = portName;
        }

        public static CommandLineOptions Parse(string[] args)
        {
            var path = args.Length > 0 ? args[0] : "programa.txt";
            var port = args.Length > 1 ? args[1] : null;

            return new CommandLineOptions(path, port);
        }
    }
}
