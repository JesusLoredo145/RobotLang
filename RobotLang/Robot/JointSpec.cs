namespace RobotLang.Robot
{
    public sealed class JointSpec
    {
        public string Name { get; init; } = "";
        public int Min { get; init; }
        public int Max { get; init; }
        public int Home { get; init; }
    }
}
