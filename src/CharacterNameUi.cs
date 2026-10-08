using UnityEngine;

namespace BetterAstralParty;

internal static class CharacterNameUi
{
    private static readonly Dictionary<IntPtr, (RuntimeObject Label, string Property, string Original, string Applied)> Changed = new();
    private static Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private static IntPtr _configClass, _helperClass, _settingsClass, _table;
    private static int _language = -1;
    private static bool _loggedApplied;
    private static readonly Stack<(RuntimeObject Node, int Depth, int Child)> Pending = new();
    private static IntPtr _root;
    private static float _scanAt;

    internal static void Tick(RuntimeObject root)
    {
        if (!Compatibility.Allowed("Names")) return;
        try
        {
            if (!Plugin.UseRealNames.Value) { RestoreAll(); return; }
            // Wait until login/config loading has finished before querying public name tables.
            if (_table == IntPtr.Zero && !GameUi.HomeAvailable) return;
            if (_root != root.Pointer) { Pending.Clear(); _root = root.Pointer; _scanAt = 0; }
            if (Pending.Count == 0)
            {
                if (Time.unscaledTime < _scanAt || !LoadNames()) return;
                foreach (var (key, edit) in Changed.ToArray())
                    if (edit.Label.Get<bool>("isDisposed")) Changed.Remove(key);
                Pending.Push((root, 0, -1));
            }
            VisitSlice();
            if (Pending.Count == 0) _scanAt = Time.unscaledTime + 0.2f;
        }
        catch (Exception ex)
        {
            Pending.Clear();
            Compatibility.Block("Names", ex, RestoreAll);
        }
    }

    private static bool LoadNames()
    {
        if (_configClass == IntPtr.Zero)
        {
            RuntimeObject.RequireClasses("Names", ("", "StaticConfigure"), ("UI", "UIHelper"), ("Core", "GameSettings"));
            _configClass = RuntimeObject.FindClass("", "StaticConfigure");
            _helperClass = RuntimeObject.FindClass("UI", "UIHelper");
            _settingsClass = RuntimeObject.FindClass("Core", "GameSettings");
        }
        if (_configClass == IntPtr.Zero || _helperClass == IntPtr.Zero || _settingsClass == IntPtr.Zero) return false;
        var table = RuntimeObject.StaticCall(_configClass, "get_Character")?.Get("InfoDict");
        if (table == null || table.Get<int>("Count") == 0) return false;
        var language = RuntimeObject.StaticField(_settingsClass, "languageType")!.Value<int>();
        if (_table == table.Pointer && _language == language) return true;
        RestoreAll();
        var pairs = new List<(string Epithet, string Name)>();
        // Protobuf's IDictionary enumerator is a reference object, unlike its boxed struct enumerator.
        var items = table.Call("System.Collections.IDictionary.GetEnumerator")!;
        while (items.Call("MoveNext")!.Value<bool>())
        {
            var config = items.Get("Value")!;
            var nameId = config.Get<int>("NameID");
            var nickId = config.Get<int>("NickID");
            if (nameId <= 0 || nickId <= 0) continue;
            var name = RuntimeObject.StaticCall(_helperClass, "GetLocal", nameId, 10)?.String() ?? "";
            var nick = RuntimeObject.StaticCall(_helperClass, "GetLocal", nickId, 10)?.String() ?? "";
            pairs.Add((nick, name));
        }
        _names = CharacterNames.BuildMap(pairs);
        _table = table.Pointer;
        _language = language;
        Plugin.Logger.LogInfo($"[캐릭터 이름] 게임 번역에서 본명/이명 {_names.Count}개 연결됨");
        return true;
    }

    private static void VisitSlice()
    {
        // Bound native UI work per frame instead of walking 4096 nodes in one update.
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var work = 0; work < 64 && Pending.Count > 0; work++)
        {
            if ((System.Diagnostics.Stopwatch.GetTimestamp() - started) / (double)System.Diagnostics.Stopwatch.Frequency >= 0.001) break;
            var (node, depth, childIndex) = Pending.Pop();
            // Field plates contain player nicknames, not real-name/epithet slots.
            if (CharacterNames.SkipSubtree(node.TypeName)) continue;
            if (depth > 24 || node.Get<bool>("isDisposed") || !node.Get<bool>("onStage")
                || !node.Get<bool>("internalVisible") || !node.Get<bool>("internalVisible2")) continue;
            if (childIndex < 0)
            {
                var field = CharacterNames.LabelField(node.TypeName);
                if (field != null)
                {
                    var label = field.Length == 0 ? node : node.Field(field);
                    if (label != null) Replace(label, CharacterNames.LabelProperty(node.TypeName));
                }
                if (depth >= 24 || node.Get("asCom") == null) continue;
                childIndex = 0;
            }
            if (childIndex >= node.Get<int>("numChildren")) continue;
            Pending.Push((node, depth, childIndex + 1));
            var child = node.Call("GetChildAt", childIndex);
            if (child != null) Pending.Push((child, depth + 1, -1));
        }
    }

    private static void Replace(RuntimeObject label, string property)
    {
        var current = ReadLabel(label, property);
        if (Changed.TryGetValue(label.Pointer, out var previous))
        {
            if (current == previous.Applied) return;
            Changed.Remove(label.Pointer); // The game reused or refreshed the label.
        }
        if (!_names.TryGetValue(current, out var name)) return;
        WriteLabel(label, property, name);
        Changed[label.Pointer] = (label, property, current, name);
        if (!_loggedApplied)
        {
            Plugin.Logger.LogInfo("[캐릭터 이름] 이름 칸에 본명 표시 적용됨 (글꼴·계정 닉네임 유지)");
            _loggedApplied = true;
        }
    }

    private static string ReadLabel(RuntimeObject label, string property)
    {
        if (property != "data") return label.Get(property)?.String() ?? "";
        var vars = label.Get("templateVars");
        return vars != null && vars.Call("ContainsKey", "data")!.Value<bool>()
            ? vars.Call("get_Item", "data")?.String() ?? "" : "";
    }

    private static void WriteLabel(RuntimeObject label, string property, string value)
    {
        if (property != "data") { label.Set(property, value); return; }
        // Preserve the localized sentence and all other variables; use the game's own renderer.
        label.Call("SetVar", "data", value);
        label.Call("FlushVars");
    }

    internal static void RestoreAll()
    {
        Pending.Clear();
        foreach (var (key, edit) in Changed.ToArray())
        {
            if (!edit.Label.Get<bool>("isDisposed"))
            {
                var current = ReadLabel(edit.Label, edit.Property);
                var restored = CharacterNames.Restore(current, edit.Original, edit.Applied);
                if (current != restored) WriteLabel(edit.Label, edit.Property, restored);
            }
            Changed.Remove(key);
        }
    }
}
