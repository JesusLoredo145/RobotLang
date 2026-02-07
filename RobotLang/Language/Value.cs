using System.Globalization;

namespace RobotLang.Language
{
    public readonly struct Value
    {
        public enum KindType { Int, String, Bool }

        public KindType Kind { get; }
        public int IntValue { get; }
        public string StringValue { get; }
        public bool BoolValue { get; }

        private Value(KindType kind, int i, string s, bool b)
        {
            Kind = kind;
            IntValue = i;
            StringValue = s;
            BoolValue = b;
        }

        public static Value FromInt(int v) => new(KindType.Int, v, "", false);
        public static Value FromString(string v) => new(KindType.String, 0, v ?? "", false);
        public static Value FromBool(bool v) => new(KindType.Bool, 0, "", v);

        public override string ToString()
        {
            return Kind switch
            {
                KindType.Int => IntValue.ToString(CultureInfo.InvariantCulture),
                KindType.String => StringValue,
                KindType.Bool => BoolValue ? "verdadero" : "falso",
                _ => ""
            };
        }

        public int AsInt(int line, int col, string? src)
        {
            if (Kind != KindType.Int) throw new LangError(line, col, "Se esperaba un entero.", src);
            return IntValue;
        }

        public bool AsBool(int line, int col, string? src)
        {
            if (Kind != KindType.Bool) throw new LangError(line, col, "Se esperaba un booleano.", src);
            return BoolValue;
        }

        public string AsString()
        {
            return Kind switch
            {
                KindType.String => StringValue,
                KindType.Int => IntValue.ToString(CultureInfo.InvariantCulture),
                KindType.Bool => BoolValue ? "true" : "false",
                _ => ""
            };
        }
    }
}
