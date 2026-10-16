namespace BetterAstralParty;

// Exact read-only API shapes used by MatchFocus.Tick. No native bridge or game execution.
internal sealed class RuntimeObject
{
    internal static RuntimeObject? Manager, Network;
    internal static bool ClassesAvailable = true;
    internal static readonly List<string> Reads = new();
    internal readonly string Kind;
    internal readonly Dictionary<string, object?> Values = new();
    internal RuntimeObject(string kind) => Kind = kind;
    internal static IntPtr FindClass(string ns, string name) => (ns, name) switch
    {
        ("GameLogic", "GameLogicManager") => ClassesAvailable ? (IntPtr)1 : IntPtr.Zero,
        ("Core.Net", "NetManager") => ClassesAvailable ? (IntPtr)2 : IntPtr.Zero,
        _ => throw new TypeLoadException(ns + "." + name)
    };
    internal static RuntimeObject? StaticField(IntPtr type, string name) => (type.ToInt64(), name) switch
    {
        (1, "_inst") => Manager, (2, "_inst") => Network,
        _ => throw new MissingFieldException(name)
    };
    private object? Read(string member, string name, Type expected)
    {
        var actual = (Kind, member, name) switch
        {
            ("Manager", "Get", "match" or "room") => typeof(RuntimeObject),
            ("Manager", "Get", "connectRoomId") => typeof(long),
            ("Network", "Get", "IsConnected") => typeof(bool),
            ("Network", "Field", "_Reconnecting") => typeof(bool),
            ("Match", "Field", "matchData") => typeof(RuntimeObject),
            ("Room", "Field", "roomController") => typeof(RuntimeObject),
            ("MatchData", "Get", "TeamId") => typeof(long),
            ("MatchData", "Get", "MatchStatus") => typeof(int),
            ("Controller", "Field", "roomStateType") => typeof(int),
            ("Controller", "Get", "localRoom") => typeof(RuntimeObject),
            ("LocalRoom", "Field", "info") => typeof(RuntimeObject),
            ("Info", "Get", "Id") => typeof(long),
            ("Info", "Get", "IsMatchRoom") => typeof(bool),
            ("Info", "Get", "MapType") => typeof(int),
            _ => throw new MissingMemberException(Kind + "." + member + "." + name)
        };
        if (actual != expected) throw new InvalidCastException(Kind + "." + name);
        Reads.Add(Kind + "." + name);
        if (!Values.TryGetValue(name, out var value)) throw new MissingFieldException(Kind + "." + name);
        if (value != null && value.GetType() != actual) throw new InvalidCastException(Kind + "." + name);
        return value;
    }
    internal RuntimeObject? Get(string name) => (RuntimeObject?)Read("Get", name, typeof(RuntimeObject));
    internal RuntimeObject? Field(string name) => (RuntimeObject?)Read("Field", name, typeof(RuntimeObject));
    internal T Get<T>(string name) where T : unmanaged => (T)Read("Get", name, typeof(T))!;
    internal T Field<T>(string name) where T : unmanaged => (T)Read("Field", name, typeof(T))!;
}
internal sealed class FocusSetting { internal bool Value = true; }
internal static class Plugin
{
    internal static readonly FocusSetting MatchFocus = new();
    internal static readonly FocusLogger Logger = new();
    internal static readonly FocusDiagnostics Diagnostics = new();
}
internal sealed class FocusLogger { internal void LogWarning(string value) { } }
internal sealed class FocusDiagnostics
{
    internal readonly Dictionary<string, string> States = new();
    internal void Error(string feature, Exception error) { }
    internal void State(string name, string value) => States[name] = value;
}
internal static class ModText { internal static string Text(string value) => value; }
