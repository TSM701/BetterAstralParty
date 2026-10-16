using BetterAstralParty.Observability; using System; using System.Linq; using System.Collections.Generic; using UnityEngine;
namespace BetterAstralParty;
internal static class ModUi {
internal static readonly Dictionary<IntPtr,(RuntimeObject Button,string Action)> Controls=new();
internal static readonly Dictionary<string,RuntimeObject> ToggleBackgrounds=new();
private static RuntimeObject? _dropdown;
private static RuntimeObject? _settingsView=new(),_settingsScroll=new();
private static float _scrollPosition,_changedAt;
private static IntPtr _pressed,_hovered;
private static bool _general=true;
private static string? _helpAction;
internal static string Help="";
internal static void Poll()=>HandleInput();
internal static void Redraw()=>Refresh();
internal static void Reset(){_pressed=_hovered=IntPtr.Zero;_scrollPosition=0;_settingsScroll!.Set("scrollingPosY",0f);_settingsScroll.Set("isDragged",false);}
internal static void Drag(bool v)=>_settingsScroll!.Set("isDragged",v);
private static void SetHelp(string action){Help=ModText.Text(MenuLayout.Help(action).Body);}
private static void TraceInput(string phase,IntPtr pointer){}
private static void ClosePanel(bool immediately=false){}
private static void HandleDropdown()=>throw new Exception("Unexpected dropdown");
private static void OpenDropdown(string action)=>throw new Exception("Unexpected dropdown");
    private static void HandleInput()
    {
        if (_dropdown != null) { HandleDropdown(); return; }
        var hit = GameUi.PointerPath().FirstOrDefault(item => Controls.ContainsKey(item.Pointer));
        var point = RuntimeObject.StaticCall(RuntimeObject.FindClass("FairyGUI", "Stage"), "get_inst")!
            .Get<Vector2>("touchPosition");
        var local = _settingsView!.Call("GlobalToLocal", point)!.Value<Vector2>();
        var position = _settingsScroll!.Get<float>("scrollingPosY");
        var scrolling = position != _scrollPosition || _settingsScroll.Get<bool>("isDragged");
        _scrollPosition = position;
        if (scrolling) { _pressed = IntPtr.Zero; hit = null; }
        if (hit != null && !MenuLayout.FixedControl(Controls[hit.Pointer].Action)
            && !MenuLayout.InSettings(local.x, local.y)) hit = null;
        var current = hit?.Pointer ?? IntPtr.Zero;
        if (current != _hovered)
        {
            if (Controls.TryGetValue(_hovered, out var old)) NativeUi.Play(old.Button, "Switchout");
            if (hit != null) NativeUi.Play(hit, "Switchin");
            _hovered = current;
            SetHelp(hit == null ? "" : Controls[current].Action);
        }
        if (Input.GetMouseButtonDown(0)) { _pressed = current; TraceInput("down", current); }
        if (!Input.GetMouseButtonUp(0)) return;
        TraceInput("up", current);
        if (current != IntPtr.Zero && current == _pressed && !hit!.Get<bool>("grayed")) Apply(Controls[current].Action);
        _pressed = IntPtr.Zero;
    }
    private static void Apply(string action)
    {
        var operation = DiagnosticOperation.New();
        var safeAction = SafeEnum.Parse<DiagnosticUiAction>(action);
        DiagnosticHub.UiAction(safeAction, DiagnosticOutcome.Begin, operation);
        DiagnosticHub.FlushSoon();
        if (Plugin.MinimalDiagnostics?.Detailed == true) _ = Plugin.MinimalDiagnostics.FlushBoundary(15);
        Plugin.Diagnostics.Write("settingsAction begin=" + action);
        try
        {
            ApplyCore(action);
            DiagnosticHub.UiAction(safeAction, DiagnosticOutcome.Completed, operation);
            Plugin.Diagnostics.Write("settingsAction complete=" + action);
        }
        catch (Exception ex)
        {
            DiagnosticHub.UiAction(safeAction, DiagnosticOutcome.Failed, operation);
            DiagnosticHub.Failure(DiagnosticFeature.Settings, DiagnosticPhase.UiAction, DiagnosticCode.Unknown, ex, operation: operation);
            Plugin.Diagnostics.Error("settingsAction." + action, ex);
            throw;
        }
    }
    private static void ApplyCore(string action)
    {
        if (!Compatibility.Allowed(action)) return;
        if (MenuLayout.Options(action).Length > 2) { OpenDropdown(action); return; }
        switch (action)
        {
            case "DiagnosticsOpen": DiagnosticActions.OpenFolder(); Refresh(); _helpAction = null; SetHelp(action); return;
            case "DiagnosticsCollect": DiagnosticActions.CreateBundle(); Refresh(); _helpAction = null; SetHelp(action); return;
            case "Close": ClosePanel(); return;
            case "Features": _general = false; _settingsScroll!.Call("ScrollTop", false); Refresh(); return;
            case "General": _general = true; _settingsScroll!.Call("ScrollTop", false); Refresh(); return;
            case "UpdateAuth": Plugin.UpdateSession?.Connect(new WindowsReleaseCredentialInput(), ModText.Korean); Refresh(); _helpAction = null; SetHelp(action); return;
            case "UpdateSignOut": Plugin.UpdateSession?.SignOut(); Refresh(); _helpAction = null; SetHelp(action); return;
            case "UpdateChannel": Plugin.UpdateChannel.Value = Plugin.UpdateChannel.Value == "Beta" ? "Stable" : "Beta"; break;
            case "UpdateCheck":
                Plugin.Updates.Check(DateTimeOffset.UtcNow);
                Refresh(); _helpAction = null; SetHelp(action); return;
            case "UpdateDownload":
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Plugin.Updates.RepositoryPage.AbsoluteUri) { UseShellExecute = true }); }
                catch { Plugin.Updates.DownloadFailed(); }
                Refresh(); _helpAction = null; SetHelp(action); return;
            case "AutoDownload": Plugin.AutoDownload.Value = !Plugin.AutoDownload.Value; break;
            case "AfterExit": Plugin.ApplyAfterExit.Value = !Plugin.ApplyAfterExit.Value; break;
            case "UpdateGet":
                if (UpdateNotificationUi.CanConfirmTransition) { UpdateNotificationUi.BeginTransition(); ClosePanel(true); return; }
                if (Plugin.Automatic.CanReactivate) Plugin.Automatic.Reactivate(); else if (Plugin.Automatic.CanRecheck) Plugin.Automatic.Recheck(); else Plugin.Automatic.Download(DateTimeOffset.UtcNow);
                Refresh(); _helpAction = null; SetHelp(action); return;
            case "UpdateCancel": Plugin.Automatic.Cancel(); Refresh(); _helpAction = null; SetHelp(action); return;
            case "Enabled": Plugin.Enabled.Value = !Plugin.Enabled.Value; break;
            case "Details": Plugin.ShowDetails.Value = !Plugin.ShowDetails.Value; break;
            case "KoMinimum": Plugin.KoMinimum.Value = !Plugin.KoMinimum.Value; if (!Plugin.KoMinimum.Value) CardDiceUi.Clear(); break;
            case "Names": Plugin.UseRealNames.Value = !Plugin.UseRealNames.Value; break;
            case "CardPopups": Plugin.CardPopups.Value = !Plugin.CardPopups.Value; break;
            case "BattleStatus": Plugin.BattleStatus.Value = !Plugin.BattleStatus.Value; break;
            case "ShushuShield": Plugin.ShushuShield.Value = !Plugin.ShushuShield.Value; if (!Plugin.ShushuShield.Value) ShushuShieldUi.Clear(); break;
            case "FieldBuffs": Plugin.FieldBuffs.Value = !Plugin.FieldBuffs.Value; break;
            case "FieldZoom": Plugin.FieldZoom.Value = !Plugin.FieldZoom.Value; if (!Plugin.FieldZoom.Value) FieldZoomUi.Clear(); break;
            case "Diagnostics": Plugin.DiagnosticLogging.Value = !Plugin.DiagnosticLogging.Value; break;
            case "MuteUnfocused": Plugin.MuteUnfocused.Value = !Plugin.MuteUnfocused.Value; break;
            case "MatchFocus": Plugin.MatchFocus.Value = !Plugin.MatchFocus.Value; break;
            case "ScaleMinus": Plugin.UiScale.Value = Math.Max(0.75f, Plugin.UiScale.Value - 0.05f); break;
            case "ScalePlus": Plugin.UiScale.Value = Math.Min(1.5f, Plugin.UiScale.Value + 0.05f); break;
            case "OpacityMinus": Plugin.Opacity.Value = Math.Max(0f, MathF.Round(Plugin.Opacity.Value - 0.05f, 2)); break;
            case "OpacityPlus": Plugin.Opacity.Value = Math.Min(1f, MathF.Round(Plugin.Opacity.Value + 0.05f, 2)); break;
        }
        _changedAt = Time.unscaledTime;
        Refresh();
        TraceInput("applied", _pressed);
        _helpAction = null;
        SetHelp(action);
    }
private static void Refresh(){
        foreach (var (button, action) in Controls.Values)
        {
            var general = MenuLayout.GeneralControl(action);
            button.Set("visible", MenuLayout.FixedControl(action) || general == _general);
            if (action is "DiagnosticsOpen" or "DiagnosticsCollect")
            {
                var busy = action == "DiagnosticsCollect" && DiagnosticActions.Busy;
                button.Set("grayed", busy); ToggleBackgrounds[action].Set("grayed", busy);
                var state = action == "DiagnosticsCollect" ? DiagnosticActions.Status : "";
                NativeUi.StyleTitle(button, ModText.Text(MenuLayout.Help(action).Title) + (state.Length == 0 ? "" : ": " + state), 22);
                continue;
            }
        }
    }
}
