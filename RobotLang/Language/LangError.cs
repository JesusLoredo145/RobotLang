using System;

namespace RobotLang.Language
{
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
}
