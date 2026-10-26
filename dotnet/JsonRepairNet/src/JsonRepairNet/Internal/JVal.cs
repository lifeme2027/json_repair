namespace JsonRepairNet.Internal;

internal abstract class JVal;

internal sealed class JNull : JVal
{
    public static readonly JNull Instance = new();

    private JNull()
    {
    }
}

internal sealed class JBool : JVal
{
    public static readonly JBool True = new(true);
    public static readonly JBool False = new(false);

    private JBool(bool value) => Value = value;

    public bool Value { get; }

    public static JBool From(bool value) => value ? True : False;
}

internal sealed class JNum : JVal
{
    public JNum(string literal) => Literal = literal;

    public string Literal { get; }
}

internal sealed class JStr : JVal
{
    public static readonly JStr Empty = new(string.Empty);

    public JStr(string value) => Value = value;

    public string Value { get; }
}

internal sealed class JArr : JVal
{
    public List<JVal> Items { get; } = [];
}

internal sealed class JObj : JVal
{
    public List<KeyValuePair<string, JVal>> Props { get; } = [];

    public void Set(string key, JVal value)
    {
        for (var i = 0; i < Props.Count; i++)
        {
            if (Props[i].Key == key)
            {
                Props[i] = new KeyValuePair<string, JVal>(key, value);
                return;
            }
        }

        Props.Add(new KeyValuePair<string, JVal>(key, value));
    }
}
