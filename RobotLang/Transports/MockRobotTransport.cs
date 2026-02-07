using System;
using System.Collections.Generic;

namespace RobotLang.Transports
{
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
}
