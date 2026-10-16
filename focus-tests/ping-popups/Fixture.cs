using System;
using System.Collections.Generic;

__LAYOUT__

sealed class RuntimeObject
{
    public string TypeName = "Fixture";
    public readonly Dictionary<string, object> Fields = new();
    public readonly List<RuntimeObject> Items = new();
    public readonly HashSet<int> Keys = new();
    public int? Count;
    public object? ValueObject;
    public static readonly Dictionary<string, int> Enums = new();
    public static int LocalType;
    public RuntimeObject? Field(string name) => Fields.GetValueOrDefault(name) as RuntimeObject;
    public T Field<T>(string name) where T : unmanaged => (T)Fields[name];
    public T Get<T>(string name) where T : unmanaged => (T)(name == "Count" ? (object)(Count ?? Items.Count) : Fields[name]);
    public T Value<T>() where T : unmanaged => (T)ValueObject!;
    public string String() => (string)ValueObject!;
    public RuntimeObject? Call(string method, params object[] args) => method switch {
        "get_Item" => Items[(int)args[0]],
        "ContainsKey" => new() { ValueObject = Keys.Contains((int)args[0]) },
        _ => throw new Exception("Unexpected game action: " + method)
    };
    public static IntPtr FindClass(string ns, string name) => ns == "UI" && name == "UIStringType" ? (IntPtr)1 : throw new Exception("Wrong enum class");
    public static RuntimeObject StaticField(IntPtr type, string name) => type == (IntPtr)1 ? new() { ValueObject = Enums[name] } : throw new Exception("Wrong class");
    public static RuntimeObject StaticCall(IntPtr type, string method, params object[] args)
    {
        if (method != "GetLocal" || args.Length != 2 || args[0] is not int || args[1] is not int) throw new Exception("Wrong localization ABI");
        LocalType = (int)args[1]; return new() { ValueObject = "localized" };
    }
__ENUM__
}

static class CardPreviewUi
{
    internal static readonly Dictionary<string, int> Pings = new();
    internal static RuntimeObject? _pingWindow;
    private static IntPtr _helperClass;
__INDEX__
__LOCAL__
    internal static string LocalForTest(RuntimeObject config, string kind) => Local(config, "NameID", kind);
    internal static void IndexForTest(RuntimeObject table) => IndexCardPings(table);
}

static class PingPopupCheck
{
    private static int assertions;
    private static void Check(bool value, string reason) { assertions++; if (!value) throw new Exception(reason); }
    private static RuntimeObject Text(string value) => new() { ValueObject = value };
    private static RuntimeObject Message(string payload, string label, int type = 8, string kind = "BattleCardMessage")
    {
        var message = new RuntimeObject { TypeName = kind };
        message.Fields["SendMsg"] = Text(payload); message.Fields["shortInfo"] = Text(label); message.Fields["MsgType"] = type;
        return message;
    }
    internal static void Run()
    {
        foreach (var text in new[] { "8, 101", "8, 101, 1", "8, 101, 2", "8, 101, 2147483647" })
            Check(CardPreviewLayout.TryCardMessage(text, 8, out var id) && id == 101, "Public native card payload " + text);
        foreach (var text in new[] { "", "8", "8,0,1", "8,-1,1", "8,101,0", "8,101,-1", "9,101,2", "8,101,2147483648", "8,101,1,2", "8,1e2,1", "8,+101,1", new string('1',129) })
            Check(!CardPreviewLayout.TryCardMessage(text, 8, out var id) && id == 0, "Invalid public payload rejected " + text);
        var history = new RuntimeObject(); var window = new RuntimeObject(); window.Fields["_messages"] = history;
        CardPreviewUi._pingWindow = window; var table = new RuntimeObject(); table.Keys.UnionWith(new[] { 101, 202 });
        foreach (var count in new[] { 1, 2, 100 }) {
            var label = "[color=#FF0000]Card 12 / 카드[/color] × " + count;
            history.Items.Clear(); history.Items.Add(Message("8, 101, " + count, label));
            CardPreviewUi.IndexForTest(table);
            Check(CardPreviewLayout.Resolve(CardPreviewUi.Pings, new(), label).Id == 101, "All public counts and native text preserved");
        }
        history.Items.Add(Message("8, 999, 2", "unknown")); history.Items.Add(Message("8, 101, 2", "not-card", kind:"BattleRelicMessage"));
        history.Items.Add(Message("9, 101, 2", "wrong-type")); CardPreviewUi.IndexForTest(table);
        Check(CardPreviewUi.Pings.Count == 1, "Unknown IDs, other messages and inconsistent types excluded");
        history.Items.Clear(); history.Items.Add(Message("8, 101, 2", "collision")); history.Items.Add(Message("8, 202, 4", "collision"));
        CardPreviewUi.IndexForTest(table); Check(CardPreviewUi.Pings["collision"] == 0, "Ambiguous native labels never guess an item");
        history.Items.RemoveAt(1); CardPreviewUi.IndexForTest(table);
        Check(CardPreviewUi.Pings["collision"] == 101, "Current displayed history is reindexed");
        Check(CardPreviewLayout.Resolve(CardPreviewUi.Pings, new() { ["collision"] = 202 }, "collision").Id == 0, "Cross-kind collision excluded");
        history.Items.Clear(); CardPreviewUi.IndexForTest(table); Check(CardPreviewUi.Pings.Count == 0, "Pooled or removed messages leave no stale index");
        foreach (var count in new[] { -1, 65 }) {
            history.Count = count; bool rejected = false;
            try { CardPreviewUi.IndexForTest(table); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Public history size bounded");
        }
        CardPreviewUi._pingWindow = null; CardPreviewUi.IndexForTest(table); Check(CardPreviewUi.Pings.Count == 0, "Missing window is harmless");
        var config = new RuntimeObject(); config.Fields["NameID"] = 123;
        foreach (var (kind, oldValue, newValue) in new[] { ("Card",9,10), ("Relic",46,47), ("Character",10,11), ("PVENurturance",43,44), ("Buff",7,7) })
        foreach (var value in new[] { oldValue, newValue }) {
            RuntimeObject.Enums[kind] = value;
            Check(CardPreviewUi.LocalForTest(config, kind) == "localized" && RuntimeObject.LocalType == value, "Localization follows native enum name " + kind);
        }
        bool missing = false;
        try { CardPreviewUi.LocalForTest(config, "Unknown"); } catch (KeyNotFoundException) { missing = true; }
        Check(missing, "Missing native enum does not fall back to a guessed number");
        Console.WriteLine("Public ping and enum fixtures passed: " + assertions);
    }
}
