using System;
using System.Collections.Generic;

namespace RobotLang.Robot
{
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
            return c;
        }
    }
}
