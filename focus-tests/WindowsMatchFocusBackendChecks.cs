using System.Text;
using System.Runtime.InteropServices;

namespace BetterAstralParty;

// The runner substitutes only P/Invoke declarations; the backend methods below test unchanged production bodies.
internal static class WindowsMatchFocusBackendChecks
{
    private static int _checks;
    private static void Check(bool valid, string message) { _checks++; if (!valid) throw new Exception(message); }
    public static void Main()
    {
        var backend = new WindowsMatchFocus();
        WindowNativeFake.Reset();
        Check(backend.FindGameWindow() == (IntPtr)42, "unique current-process top-level visible Unity window");
        Check(backend.Activate((IntPtr)42) && WindowNativeFake.Foreground == (IntPtr)42, "normal foreground request confirms actual HWND");
        Check(WindowNativeFake.Positions == 2 && WindowNativeFake.Activations == 1 && WindowNativeFake.ExtendedStyle == 0,
            "activation pulse restores original non-topmost style before returning");
        Check(WindowNativeFake.Events.SequenceEqual(new[] { "position:-1", "activate", "position:-2" }),
            "synchronous TOPMOST precedes activation and NOTOPMOST always follows");
        Check(WindowNativeFake.PositionRequests.All(request => request.Flags == 0x213 && request.Geometry == (0, 0, 0, 0)),
            "temporary pulse never moves, resizes, activates or changes owner order asynchronously");
        Check(WindowNativeFake.StyleIndexes.All(index => index == -20), "reads only GWL_EXSTYLE");
        Check(backend.Raise((IntPtr)42), "confirmed foreground can be raised");
        Check(WindowNativeFake.Positions == 3 && WindowNativeFake.LastFlags == 0x4213 && WindowNativeFake.LastAfter == IntPtr.Zero,
            "raise uses HWND_TOP async nonactivating nonmoving nonsizing nonowner flags");
        Check(WindowNativeFake.LastGeometry == (0, 0, 0, 0) && WindowNativeFake.Activations == 1,
            "raise preserves dimensions and never sends another focus request");

        foreach (var invalid in new[] { "zero", "foreign", "hidden", "child", "class", "dead", "background", "minimized" })
        {
            WindowNativeFake.Reset(); WindowNativeFake.Foreground = (IntPtr)42;
            var window = (IntPtr)42;
            switch (invalid)
            {
                case "zero": window = IntPtr.Zero; break;
                case "foreign": WindowNativeFake.Process = (uint)Environment.ProcessId + 1; break;
                case "hidden": WindowNativeFake.Visible = false; break;
                case "child": WindowNativeFake.Ancestor = (IntPtr)43; break;
                case "class": WindowNativeFake.ClassName = "Dialog"; break;
                case "dead": WindowNativeFake.Alive = false; break;
                case "background": WindowNativeFake.Foreground = (IntPtr)99; break;
                case "minimized": WindowNativeFake.Minimized = true; break;
            }
            Check(!backend.Raise(window) && WindowNativeFake.Positions == 0, "no raise on invalid target: " + invalid);
            Check(WindowNativeFake.Activations == 0 && WindowNativeFake.Flashes == 0, "guard has no activation/flash: " + invalid);
        }
        WindowNativeFake.Reset(); WindowNativeFake.Foreground = (IntPtr)42; WindowNativeFake.AcceptPosition = false;
        Check(!backend.Raise((IntPtr)42) && WindowNativeFake.Positions == 1, "SetWindowPos refusal is not presentation success");
        foreach (var changed in new[] { "foreground", "ownership", "destroyed", "minimized" })
        {
            WindowNativeFake.Reset(); WindowNativeFake.Foreground = (IntPtr)42; WindowNativeFake.AfterPosition = () =>
            {
                if (changed == "foreground") WindowNativeFake.Foreground = (IntPtr)99;
                if (changed == "ownership") WindowNativeFake.Process = (uint)Environment.ProcessId + 1;
                if (changed == "destroyed") WindowNativeFake.Alive = false;
                if (changed == "minimized") WindowNativeFake.Minimized = true;
            };
            Check(!backend.Raise((IntPtr)42), "post-request target is revalidated: " + changed);
            Check(WindowNativeFake.Positions == 1 && WindowNativeFake.Activations == 0, "race does not retry or take foreign focus: " + changed);
        }
        WindowNativeFake.Reset(); WindowNativeFake.AcceptActivation = false;
        Check(!backend.Activate((IntPtr)42) && WindowNativeFake.Positions == 2 && WindowNativeFake.ExtendedStyle == 0,
            "OS activation refusal still restores temporary topmost and never pretends success");
        WindowNativeFake.Reset(); WindowNativeFake.Minimized = true;
        Check(backend.Restore((IntPtr)42) && WindowNativeFake.Restores == 1 && WindowNativeFake.RestoreCommand == 9,
            "restore stays asynchronous SW_RESTORE");
        WindowNativeFake.Reset(); WindowNativeFake.Windows = new[] { (IntPtr)42, (IntPtr)43 }; WindowNativeFake.ValidAnyWindow = true;
        Check(backend.FindGameWindow() == IntPtr.Zero, "ambiguous Unity windows rejected");
        WindowNativeFake.Reset(); WindowNativeFake.EnumerationCompleted = false;
        Check(backend.FindGameWindow() == IntPtr.Zero, "incomplete native enumeration rejected");
        WindowNativeFake.Reset(); backend.Flash((IntPtr)42);
        Check(WindowNativeFake.Flashes == 1, "background fallback flashes only verified own window");
        WindowNativeFake.Foreground = (IntPtr)42; backend.Flash((IntPtr)42);
        Check(WindowNativeFake.Flashes == 1, "foreground game is never flashed");
        PulseChecks();
        OwnedPopupChecks();
        Console.WriteLine($"PASS: {_checks} matching-focus exact-backend Win32-substitute assertions; native window calls not executed.");
    }
    private static void PulseChecks()
    {
        var backend = new WindowsMatchFocus();
        WindowNativeFake.Reset(); WindowNativeFake.ExtendedStyle = 8;
        Check(backend.Activate((IntPtr)42) && WindowNativeFake.ExtendedStyle == 8 && WindowNativeFake.Positions == 0,
            "original TOPMOST window is activated without promotion or demotion");
        Check(backend.RestoreTopmost() && WindowNativeFake.Positions == 0, "cleanup never demotes original topmost");
        Check(backend.Raise((IntPtr)42) && WindowNativeFake.ExtendedStyle == 8 && WindowNativeFake.LastAfter == IntPtr.Zero,
            "ordinary final raise also preserves original TOPMOST");
        WindowNativeFake.Reset(); WindowNativeFake.ExtendedStyle = 0x40000;
        Check(backend.Activate((IntPtr)42) && WindowNativeFake.ExtendedStyle == 0x40000,
            "pulse preserves unrelated extended-style bits");
        WindowNativeFake.Reset(); WindowNativeFake.StyleError = 5;
        Check(!backend.Activate((IntPtr)42) && WindowNativeFake.Activations == 0 && WindowNativeFake.Positions == 0,
            "failed ambiguous zero style read cannot authorize promotion");
        foreach (var invalid in new[] { "zero", "foreign", "hidden", "child", "class", "dead" })
        {
            backend = new(); WindowNativeFake.Reset(); var handle = (IntPtr)42;
            switch (invalid)
            {
                case "zero": handle = IntPtr.Zero; break;
                case "foreign": WindowNativeFake.Process++; break;
                case "hidden": WindowNativeFake.Visible = false; break;
                case "child": WindowNativeFake.Ancestor = (IntPtr)43; break;
                case "class": WindowNativeFake.ClassName = "Dialog"; break;
                case "dead": WindowNativeFake.Alive = false; break;
            }
            Check(!backend.Activate(handle) && WindowNativeFake.Positions == 0 && WindowNativeFake.Activations == 0,
                "pulse cannot target invalid Unity window: " + invalid);
        }
        foreach (var refusal in new[] { "promotion", "activation", "rollback", "rollback-style" })
        {
            backend = new(); WindowNativeFake.Reset();
            if (refusal == "promotion") { WindowNativeFake.AcceptPromotion = false; WindowNativeFake.ApplyRefusedPosition = true; }
            if (refusal == "activation") WindowNativeFake.AcceptActivation = false;
            if (refusal == "rollback") WindowNativeFake.AcceptRollback = false;
            if (refusal == "rollback-style") WindowNativeFake.ApplyRollback = false;
            Check(!backend.Activate((IntPtr)42), "native refusal or unconfirmed rollback cannot report activation: " + refusal);
            Check(refusal != "promotion" || WindowNativeFake.Activations == 0, "refused promotion never proceeds to activation");
            if (refusal is "rollback" or "rollback-style")
            {
                Check(WindowNativeFake.ExtendedStyle == 8, "uncertain rollback remains owned: " + refusal);
                var activations = WindowNativeFake.Activations;
                WindowNativeFake.AcceptRollback = WindowNativeFake.ApplyRollback = true;
                Check(backend.RestoreTopmost() && WindowNativeFake.ExtendedStyle == 0 && WindowNativeFake.Activations == activations,
                    "next cleanup retries only NOTOPMOST, not activation: " + refusal);
                var positions = WindowNativeFake.Positions;
                Check(backend.RestoreTopmost() && WindowNativeFake.Positions == positions, "completed rollback is idle: " + refusal);
            }
            else Check(WindowNativeFake.ExtendedStyle == 0 && backend.RestoreTopmost(), "refusal leaves no persistent topmost: " + refusal);
        }
        foreach (var race in new[] { "hidden", "foreign", "dead", "modal" })
        {
            backend = new(); WindowNativeFake.Reset(); WindowNativeFake.AfterPosition = () =>
            {
                if (WindowNativeFake.LastAfter != (IntPtr)(-1)) return;
                if (race == "hidden") WindowNativeFake.Visible = false;
                if (race == "foreign") WindowNativeFake.Process++;
                if (race == "dead") WindowNativeFake.Alive = false;
                if (race == "modal") { WindowNativeFake.ValidAnyWindow = true; WindowNativeFake.Foreground = (IntPtr)43; }
            };
            Check(!backend.Activate((IntPtr)42) && WindowNativeFake.Activations == 0,
                "promotion race cannot activate invalid target or steal own-modal focus: " + race);
            Check(WindowNativeFake.Positions == (race is "hidden" or "modal" ? 2 : 1),
                "promotion race restores own target only, never foreign/dead HWND: " + race);
        }
        foreach (var race in new[] { "hidden", "foreign", "dead", "class", "child" })
        {
            backend = new(); WindowNativeFake.Reset(); WindowNativeFake.AcceptRollback = false;
            _ = backend.Activate((IntPtr)42); WindowNativeFake.AcceptRollback = true;
            var positions = WindowNativeFake.Positions;
            switch (race)
            {
                case "hidden": WindowNativeFake.Visible = false; break;
                case "foreign": WindowNativeFake.Process++; break;
                case "dead": WindowNativeFake.Alive = false; break;
                case "class": WindowNativeFake.ClassName = "Dialog"; break;
                case "child": WindowNativeFake.Ancestor = (IntPtr)43; break;
            }
            Check(backend.RestoreTopmost(), "cleanup finishes or abandons only no-longer-owned handle: " + race);
            Check(WindowNativeFake.Positions == positions + (race == "hidden" ? 1 : 0), "hidden own window restored; foreign/reused target never written: " + race);
            Check(race != "hidden" || WindowNativeFake.ExtendedStyle == 0, "hidden Unity rollback succeeds");
        }
        foreach (var failure in new[] { "promotion", "activation", "rollback" })
        {
            backend = new(); WindowNativeFake.Reset();
            WindowNativeFake.ThrowPromotion = failure == "promotion";
            WindowNativeFake.ThrowActivation = failure == "activation";
            WindowNativeFake.ThrowRollback = failure == "rollback";
            var thrown = false;
            try { _ = backend.Activate((IntPtr)42); } catch (InvalidOperationException) { thrown = true; }
            Check(thrown, "substitute exception reaches managed caller: " + failure);
            if (failure == "rollback") Check(WindowNativeFake.ExtendedStyle == 8, "throwing rollback retains ownership");
            else Check(WindowNativeFake.ExtendedStyle == 0, "finally restores uncertain promotion/activation: " + failure);
            WindowNativeFake.ThrowPromotion = WindowNativeFake.ThrowActivation = WindowNativeFake.ThrowRollback = false;
            Check(backend.RestoreTopmost() && WindowNativeFake.ExtendedStyle == 0, "exception recovery cannot leave lifetime topmost: " + failure);
        }
        backend = new(); WindowNativeFake.Reset(); WindowNativeFake.AcceptRollback = false;
        _ = backend.Activate((IntPtr)42); var before = WindowNativeFake.Positions;
        Check(!backend.Raise((IntPtr)42) && WindowNativeFake.Positions == before + 1 && WindowNativeFake.LastAfter == (IntPtr)(-2),
            "Raise repairs owned topmost first and cannot enqueue ordinary raise while restoration fails");
        WindowNativeFake.AcceptRollback = true;
        Check(backend.Raise((IntPtr)42) && WindowNativeFake.ExtendedStyle == 0 && WindowNativeFake.LastAfter == IntPtr.Zero,
            "repaired original style precedes ordinary foreground raise");
    }
    private static void OwnedPopupChecks()
    {
        foreach (var scenario in new[] { "visible-topmost", "hidden-topmost", "nested-topmost", "non-topmost", "unowned-topmost", "foreign-unowned-topmost", "style-failure", "enum-failure" })
        {
            var backend = new WindowsMatchFocus(); WindowNativeFake.Reset();
            var popup = new WindowNativeFake.ExtraWindow
            {
                Owner = scenario.Contains("unowned", StringComparison.Ordinal) ? IntPtr.Zero : (IntPtr)42,
                Visible = scenario != "hidden-topmost",
                ExtendedStyle = scenario is "non-topmost" or "style-failure" ? 0 : 8,
                StyleError = scenario == "style-failure" ? 5 : 0,
                Process = (uint)Environment.ProcessId + (scenario == "foreign-unowned-topmost" ? 1u : 0u)
            };
            WindowNativeFake.Extras[(IntPtr)43] = popup;
            WindowNativeFake.Windows = new[] { (IntPtr)42, (IntPtr)43 };
            if (scenario == "nested-topmost")
            {
                popup.Owner = (IntPtr)44;
                WindowNativeFake.Extras[(IntPtr)44] = new() { Owner = (IntPtr)42 };
                WindowNativeFake.Windows = new[] { (IntPtr)42, (IntPtr)44, (IntPtr)43 };
            }
            WindowNativeFake.EnumerationCompleted = scenario != "enum-failure";
            var pulse = scenario is "non-topmost" or "unowned-topmost" or "foreign-unowned-topmost";
            var original = popup.ExtendedStyle;
            Check(backend.Activate((IntPtr)42) && WindowNativeFake.Activations == 1,
                "unsafe pulse falls back to ordinary allowed foreground request: " + scenario);
            Check(WindowNativeFake.Positions == (pulse ? 2 : 0) && WindowNativeFake.ExtendedStyle == 0 && popup.ExtendedStyle == original,
                "root and existing popup topmost styles are preserved: " + scenario);
            Check(WindowNativeFake.Events.SequenceEqual(pulse ? new[] { "position:-1", "activate", "position:-2" } : new[] { "activate" }),
                "no promotion or demotion when owner style cannot safely be restored: " + scenario);
            Check(WindowNativeFake.AncestorReads.Contains(((IntPtr)43, 3u)), "owner check uses GA_ROOTOWNER, not GA_ROOT: " + scenario);
            Check(backend.RestoreTopmost() && WindowNativeFake.Positions == (pulse ? 2 : 0),
                "fallback has no latent rollback ownership: " + scenario);
        }
        var alreadyTopmost = new WindowsMatchFocus(); WindowNativeFake.Reset(); WindowNativeFake.ExtendedStyle = 8;
        WindowNativeFake.EnumerationCompleted = false;
        Check(alreadyTopmost.Activate((IntPtr)42) && WindowNativeFake.Positions == 0 && WindowNativeFake.ExtendedStyle == 8,
            "original root TOPMOST remains untouched even when ownership enumeration is unavailable");
        var ownedUnity = new WindowsMatchFocus(); WindowNativeFake.Reset(); WindowNativeFake.UnityRootOwner = (IntPtr)44;
        Check(ownedUnity.Activate((IntPtr)42) && WindowNativeFake.Activations == 1 && WindowNativeFake.Positions == 0
            && WindowNativeFake.ExtendedStyle == 0 && WindowNativeFake.AncestorReads.Contains(((IntPtr)42, 3u)),
            "Unity owned by another root uses ordinary activation without an unsafe pulse");
        foreach (var race in new[] { "foreign", "dead", "hidden", "class", "child", "minimized", "modal" })
        {
            var backend = new WindowsMatchFocus(); WindowNativeFake.Reset();
            WindowNativeFake.EnumerationCompleted = race != "modal";
            WindowNativeFake.AfterEnumeration = () =>
            {
                if (race == "foreign") WindowNativeFake.Process++;
                if (race == "dead") WindowNativeFake.Alive = false;
                if (race == "hidden") WindowNativeFake.Visible = false;
                if (race == "class") WindowNativeFake.ClassName = "Dialog";
                if (race == "child") WindowNativeFake.Ancestor = (IntPtr)43;
                if (race == "minimized") WindowNativeFake.Minimized = true;
                if (race == "modal") { WindowNativeFake.ValidAnyWindow = true; WindowNativeFake.Foreground = (IntPtr)43; }
            };
            Check(!backend.Activate((IntPtr)42) && WindowNativeFake.Positions == 0 && WindowNativeFake.Activations == 0,
                "enumeration race is revalidated before promotion or fallback activation: " + race);
        }
        var nativeStyleChanged = new WindowsMatchFocus(); WindowNativeFake.Reset();
        WindowNativeFake.AfterEnumeration = () => WindowNativeFake.ExtendedStyle = 8;
        Check(nativeStyleChanged.Activate((IntPtr)42) && WindowNativeFake.Positions == 0 && WindowNativeFake.ExtendedStyle == 8,
            "native TOPMOST becoming active during enumeration remains untouched");
    }
}

internal static class WindowNativeFake
{
    internal static bool Alive, Visible, Minimized, ValidAnyWindow, AcceptActivation, AcceptPosition, EnumerationCompleted;
    internal static bool AcceptPromotion, AcceptRollback, ApplyRollback, ApplyRefusedPosition, ThrowPromotion, ThrowActivation, ThrowRollback;
    internal static int ExtendedStyle, StyleError;
    internal static readonly List<int> StyleIndexes = new();
    internal static readonly List<string> Events = new();
    internal static readonly List<(uint Flags, (int, int, int, int) Geometry)> PositionRequests = new();
    internal static readonly List<(IntPtr Window, uint Flags)> AncestorReads = new();
    internal static readonly Dictionary<IntPtr, ExtraWindow> Extras = new();
    internal sealed class ExtraWindow
    {
        internal IntPtr Owner = (IntPtr)42;
        internal bool Visible = true;
        internal int ExtendedStyle, StyleError;
        internal uint Process = (uint)Environment.ProcessId;
        internal string ClassName = "#32770";
    }
    internal static uint Process, LastFlags;
    internal static IntPtr Foreground, Ancestor, LastAfter;
    internal static IntPtr UnityRootOwner;
    internal static IntPtr[] Windows = Array.Empty<IntPtr>();
    internal static string ClassName = "";
    internal static int Activations, Positions, Restores, RestoreCommand, Flashes;
    internal static (int, int, int, int) LastGeometry;
    internal static Action? AfterPosition, AfterEnumeration;
    internal static void Reset()
    {
        Alive = Visible = AcceptActivation = AcceptPosition = EnumerationCompleted = true;
        Minimized = ValidAnyWindow = false; Process = (uint)Environment.ProcessId;
        Foreground = (IntPtr)99; Ancestor = (IntPtr)42; Windows = new[] { (IntPtr)42 }; ClassName = "UnityWndClass";
        Activations = Positions = Restores = RestoreCommand = Flashes = 0; LastFlags = 0; LastAfter = IntPtr.Zero;
        LastGeometry = default; AfterPosition = AfterEnumeration = null;
        AcceptPromotion = AcceptRollback = ApplyRollback = true;
        ApplyRefusedPosition = ThrowPromotion = ThrowActivation = ThrowRollback = false;
        ExtendedStyle = StyleError = 0; StyleIndexes.Clear(); Events.Clear(); PositionRequests.Clear();
        Extras.Clear(); AncestorReads.Clear();
        UnityRootOwner = (IntPtr)42;
    }
    internal static bool Enumerate(Delegate callback, IntPtr parameter)
    {
        foreach (var window in Windows) if (!(bool)callback.DynamicInvoke(window, parameter)!) return false;
        AfterEnumeration?.Invoke();
        return EnumerationCompleted;
    }
    internal static bool IsWindow(IntPtr window) => Alive && window != IntPtr.Zero && (ValidAnyWindow || window == (IntPtr)42 || Extras.ContainsKey(window));
    internal static bool IsVisible(IntPtr window) => Extras.TryGetValue(window, out var extra) ? extra.Visible : Visible;
    internal static bool IsIconic(IntPtr window) => Minimized;
    internal static IntPtr GetAncestor(IntPtr window, uint flags)
    {
        AncestorReads.Add((window, flags));
        if (flags == 3) return RootOwner(window);
        if (Extras.ContainsKey(window)) return window;
        return ValidAnyWindow ? window : Ancestor;
    }
    private static IntPtr RootOwner(IntPtr window)
    {
        for (var count = 0; count < 16 && Extras.TryGetValue(window, out var extra) && extra.Owner != IntPtr.Zero; count++) window = extra.Owner;
        return window == (IntPtr)42 ? UnityRootOwner : window;
    }
    internal static uint GetProcess(IntPtr window, out uint process) { process = Extras.TryGetValue(window, out var extra) ? extra.Process : Process; return IsWindow(window) ? 7u : 0; }
    internal static int GetClassName(IntPtr window, StringBuilder name, int length) { var value = Extras.TryGetValue(window, out var extra) ? extra.ClassName : ClassName; name.Append(value); return value.Length; }
    internal static bool Restore(IntPtr window, int command) { Restores++; RestoreCommand = command; Minimized = false; return true; }
    internal static int Style(IntPtr window, int index)
    {
        StyleIndexes.Add(index);
        if (Extras.TryGetValue(window, out var extra)) { Marshal.SetLastPInvokeError(extra.StyleError); return extra.ExtendedStyle; }
        Marshal.SetLastPInvokeError(StyleError); return ExtendedStyle;
    }
    internal static bool Activate(IntPtr window)
    {
        Activations++; Events.Add("activate"); if (ThrowActivation) throw new InvalidOperationException("activation");
        if (AcceptActivation) Foreground = window; return AcceptActivation;
    }
    internal static bool Position(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags)
    {
        Positions++; LastAfter = after; LastFlags = flags; LastGeometry = (x, y, width, height);
        Events.Add("position:" + after.ToInt64()); PositionRequests.Add((flags, LastGeometry));
        var accepted = AcceptPosition && (after == (IntPtr)(-1) ? AcceptPromotion : after != (IntPtr)(-2) || AcceptRollback);
        if (after == (IntPtr)(-2) && ThrowRollback) throw new InvalidOperationException("rollback");
        if (after == (IntPtr)(-1) && (accepted || ApplyRefusedPosition)) ExtendedStyle |= 8;
        if (after == (IntPtr)(-2) && ApplyRollback && (accepted || ApplyRefusedPosition))
        {
            ExtendedStyle &= ~8;
            foreach (var extra in Extras.Where(pair => RootOwner(pair.Key) == window).Select(pair => pair.Value)) extra.ExtendedStyle &= ~8;
        }
        AfterPosition?.Invoke();
        if (after == (IntPtr)(-1) && ThrowPromotion) throw new InvalidOperationException("promotion");
        return accepted;
    }
    internal static bool Flash(IntPtr window) { Flashes++; return false; }
}
