using System;
using System.Text;
using RobotLang.App;

namespace RobotLang
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            var runner = new AppRunner();
            return runner.Run(args);
        }
    }
}
