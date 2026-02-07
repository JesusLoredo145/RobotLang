using System;
using System.Collections.Generic;

namespace RobotLang.Language
{
    public sealed class RuntimeState
    {
        public Dictionary<string, Value> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

        // logical state of the arm, in degrees or units
        public Dictionary<string, int> JointPositions { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
