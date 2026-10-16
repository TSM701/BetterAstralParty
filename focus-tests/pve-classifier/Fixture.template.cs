#nullable enable
using System;
using System.Collections.Generic;

namespace PveClassifierFixture
{
    internal static class VisibleCombatAdvisor
    {
__PURE_BRIDGE__
    }
    internal sealed class RuntimeObject
    {
        internal static readonly Dictionary<int, RuntimeObject> Rows = new();
        private static readonly RuntimeObject Table = new(0, 0), DictionaryNode = new(0, 0);
        internal static bool HelperAvailable = true, ConfigurationClassAvailable = true, TableReady = true, DictionaryReady = true;
        internal static Exception? LookupError, ReadError, ConfigurationError;
        internal static int Lookups, ModeReads, GroupReads, ConfigurationReads, DictionaryReads;
        internal int Mode, Group;
        internal RuntimeObject(int mode, int group) { Mode = mode; Group = group; }
        internal static void Reset()
        {
            Rows.Clear(); HelperAvailable = ConfigurationClassAvailable = TableReady = DictionaryReady = true;
            LookupError = ReadError = ConfigurationError = null;
            Lookups = ModeReads = GroupReads = ConfigurationReads = DictionaryReads = 0;
        }
        private static IntPtr FindClass(string ns, string name)
        {
            if (ns == "UI" && name == "UIHelper") return HelperAvailable ? (IntPtr)1 : IntPtr.Zero;
            if (ns == "" && name == "StaticConfigure") return ConfigurationClassAvailable ? (IntPtr)2 : IntPtr.Zero;
            throw new InvalidOperationException("Unexpected class read.");
        }
        private static RuntimeObject? StaticCall(IntPtr klass, string method, params object?[] args)
        {
            if (klass == (IntPtr)2 && method == "get_GameMode" && args.Length == 0) {
                ConfigurationReads++; if (ConfigurationError != null) throw ConfigurationError;
                return TableReady ? Table : null;
            }
            if (klass != (IntPtr)1 || method != "GetGameModeInfoConfigure" || args.Length != 1 || args[0] is not int mode)
                throw new InvalidOperationException("Unexpected native call; only the public requested-row lookup is allowed.");
            Lookups++; if (LookupError != null) throw LookupError;
            return Rows.TryGetValue(mode, out var row) ? row : null;
        }
        private RuntimeObject? Get(string name)
        {
            if (!ReferenceEquals(this, Table) || name != "InfoDict") throw new InvalidOperationException("Unexpected public readiness property.");
            DictionaryReads++; return DictionaryReady ? DictionaryNode : null;
        }
        private T Get<T>(string name) where T : unmanaged
        {
            if (ReadError != null) throw ReadError;
            if (typeof(T) != typeof(int)) throw new InvalidOperationException("Unexpected public value type.");
            if (name == "MapModeType") { ModeReads++; return (T)(object)Mode; }
            if (name == "MapModeGroup") { GroupReads++; return (T)(object)Group; }
            throw new InvalidOperationException("Unexpected public property.");
        }
__RUNTIME_CLASSIFIER__
    }
    internal static class LoaderFixture
    {
        internal static void LoadCore(Action firstGate)
        {
__BINDING__
            firstGate();
        }
    }
    public static class Checks
    {
        public static int Assertions;
        private static void Check(bool value, string name)
        { if (!value) throw new InvalidOperationException(name); Assertions++; }
        private static bool IsPve(int mode) => VisibleCombatAdvisor.IsPve(mode);
        public static void Run()
        {
            RuntimeObject.Reset(); VisibleCombatAdvisor.PveClassifier = null;
            foreach (var mode in new[] { -1, 0, 1, 4, 6, 9, 10, 12, 13, 12345 })
                Check(IsPve(mode) == (mode is 4 or 6 or 9 or 10 or 12), "Historical standalone defaults remain unchanged before runtime binding.");
            var firstGateObserved = false;
            LoaderFixture.LoadCore(() => {
                firstGateObserved = true;
                Check(VisibleCombatAdvisor.PveClassifier != null && !IsPve(4), "Actual binding denies an unclassified historical mode before the first loader gate.");
            });
            Check(firstGateObserved, "First gate executes only after the literal production binding.");
            RuntimeObject.Reset(); RuntimeObject.HelperAvailable = false;
            Check(!IsPve(4) && !IsPve(12345) && RuntimeObject.Lookups == 0 && RuntimeObject.ConfigurationReads == 0, "Missing native helper fails closed without invoking a public lookup or using old membership.");
            RuntimeObject.Rows[4] = new(4, 1); RuntimeObject.HelperAvailable = true;
            Check(IsPve(4), "A late native helper can classify a public row without retaining an unavailable result.");
            foreach (var unavailable in new[] { "class", "table", "dictionary" }) {
                RuntimeObject.Reset(); RuntimeObject.Rows[4] = new(4, 1);
                RuntimeObject.ConfigurationClassAvailable = unavailable != "class";
                RuntimeObject.TableReady = unavailable != "table"; RuntimeObject.DictionaryReady = unavailable != "dictionary";
                Check(!IsPve(4) && RuntimeObject.Lookups == 0, "Unready public configuration fails closed before requested-row lookup: " + unavailable);
                if (unavailable == "class") Check(RuntimeObject.ConfigurationReads == 0, "Missing configuration class never receives a static native call.");
                if (unavailable == "table") Check(RuntimeObject.DictionaryReads == 0, "Missing native table never receives a property read.");
                RuntimeObject.ConfigurationClassAvailable = RuntimeObject.TableReady = RuntimeObject.DictionaryReady = true;
                Check(IsPve(4), "Late public configuration readiness recovers without caching a false result: " + unavailable);
            }
            RuntimeObject.Rows.Clear();
            Check(!IsPve(4) && !IsPve(12345), "Missing rows fail closed; the native missing-row true fallback is not followed.");
            RuntimeObject.Rows[4] = new(6, 1); RuntimeObject.ModeReads = RuntimeObject.GroupReads = 0;
            Check(!IsPve(4) && RuntimeObject.ModeReads == 1 && RuntimeObject.GroupReads == 0, "A different mode's PvE row cannot authorize the requested mode.");
            foreach (var group in new[] { -1, 0, 2, 3, 99 }) {
                RuntimeObject.Rows[4] = new(4, group);
                Check(!IsPve(4), "PvP, unset and unknown groups never fall back to historical membership.");
            }
            RuntimeObject.Rows[4] = new(4, 1); Check(IsPve(4), "Matching public mode identity in group one is accepted.");
            RuntimeObject.Rows[12345] = new(12345, 1);
            Check(IsPve(12345), "A newly configured PvE mode is accepted without a numeric whitelist.");
            RuntimeObject.Rows[12345] = new(4, 1); Check(!IsPve(12345), "Mode identity validation also rejects mismatched new-mode rows.");
            RuntimeObject.Rows.Remove(12345); var before = RuntimeObject.Lookups;
            Check(!IsPve(12345), "Absent future row remains unclassified.");
            var live = new RuntimeObject(12345, 1); RuntimeObject.Rows[12345] = live;
            Check(IsPve(12345), "Late public configuration becomes usable without retaining a failed lookup.");
            live.Group = 2; Check(!IsPve(12345), "Live public group changes are observed without retaining a positive classification.");
            live.Group = 1; Check(IsPve(12345), "Live public group recovery is observed.");
            RuntimeObject.Rows.Remove(12345);
            Check(!IsPve(12345) && RuntimeObject.Lookups - before == 5, "Removed rows are not cached and every classification uses a fresh public lookup.");
            var fault = new InvalidOperationException("Inert public-lookup failure"); RuntimeObject.LookupError = fault; var propagated = false;
            try { IsPve(4); } catch (InvalidOperationException error) { propagated = ReferenceEquals(error, fault); }
            Check(propagated, "Native lookup exceptions propagate to existing feature handling instead of authorizing a fallback.");
            RuntimeObject.LookupError = null; RuntimeObject.ReadError = fault; propagated = false;
            try { IsPve(4); } catch (InvalidOperationException error) { propagated = ReferenceEquals(error, fault); }
            Check(propagated, "Public row-read errors preserve the original failure instead of changing classification.");
            RuntimeObject.ReadError = null; RuntimeObject.ConfigurationError = fault; propagated = false;
            try { IsPve(4); } catch (InvalidOperationException error) { propagated = ReferenceEquals(error, fault); }
            Check(propagated, "Actual readiness-call errors retain their original failure for existing feature handling.");
            RuntimeObject.ConfigurationError = null;
            VisibleCombatAdvisor.PveClassifier = _ => false; Check(!IsPve(4), "A bound false result is not a nullable or historical-default fallback.");
            VisibleCombatAdvisor.PveClassifier = mode => mode == 12345;
            Check(IsPve(12345) && !IsPve(4), "The shared pure bridge follows the exact bound classification.");
            VisibleCombatAdvisor.PveClassifier = null;
        }
    }
}
