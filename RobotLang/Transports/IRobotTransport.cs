using System.Collections.Generic;

namespace RobotLang.Transports
{
    public interface IRobotTransport
    {
        void MoveJoint(string joint, int target);
        void HomeAll(Dictionary<string, int> homePositions);
        void Gripper(string action);
        void WaitMs(int ms);
    }
}
