using UnityEngine;

namespace BetterAstralParty;

// 전투 PIP 첫 안내 / Fight PiP first-time tutorial (user-designed 2026-11-18; drafts artifacts/cc/cctv-fight/v7-hint to
// v12-tutorial-560, rules in .claude/rules/fight-pip.md). Once, the first time a watched fight opens full screen while Fight
// PiP waits (Start View 전체 화면, "PIP 모드" not pressed in the match): 0.8 s after the battle platform opens, and only in the
// first 3 s of it, a translucent black screen covers the game with the "PIP 모드" capsule left lit above it in a pulsing
// yellow ring, the music turns down, and a cream card under the capsule shows the small window over the field (a masked
// capture, FightPipTutorialImage), what it does and both ways in the on-screen labels' words. Only 확인 closes it; Esc or the
// capsule shrink to the small window and also finish it; the fight ending finishes it once it has shown 3 s, else it comes
// back at the next such fight. Skipped in replays, while spectating someone else's match, while the game is not in front or
// something covers the fight, and for players who already know the PiP (pressed "PIP 모드" or Esc, moved or sized the
// window, or set Start View to 작은 창). Development builds: Ctrl+Shift+F5 rearms it and, during a waiting full-screen fight,
// shows it at once. Own breaker; a failure never touches the PiP.
internal static class FightPipTutorial
{
    internal const string Feature = "FightPipTutorial";
    private static readonly bool DevelopmentBuild = Plugin.Version.Contains("-dev.", StringComparison.Ordinal);

    // What FightPip's session says each tick; Token is the session object, so a new fight is a new token.
    internal readonly record struct Fight(object Token, RuntimeObject Root, RuntimeObject Window, RuntimeObject? Corner,
        bool Waiting, bool Expanded, bool Closing, float PlatformAt);

    private const float ShowDelay = .8f, ShowLatest = 3f, CountAfter = 3f;
    private const float DimIn = .4f, CardDelay = .2f, CardIn = .35f, CardOut = .3f, DimOut = .4f, PulsePeriod = 1.2f;
    private const float DimAlpha = .62f, MusicLeft = .35f;
    private const int Order = 9000; // Over the fight and its toolbars, under the mod's own windows (28000 and up).
    // Root units, the card's draft (v12-tutorial-560): the capsule FightPip draws at (24, 21), 44 high.
    private const float CardX = 24f, CardY = 21f + 44f + 18f, Pad = 20f, ImageW = 560f, ImageH = 315f;
    private const float CardW = Pad + ImageW + Pad, ButtonW = 104f, ButtonH = 40f;
    private const float CardH = Pad + ImageH + 16f + 28f + 2 * 25f + 6f + 2 * 21f + 16f + ButtonH + Pad;
    private const float TailX = 24f + 31f; // under the capsule's PiP icon
    private static readonly Color Clear0 = new(0f, 0f, 0f, 0f), Black = Color.black;
    private static readonly Color Yellow = new(1f, 214f / 255, 64f / 255, 1f), YellowHover = new(1f, 230f / 255, 125f / 255, 1f);
    private static readonly Color YellowPress = new(236f / 255, 190f / 255, 36f / 255, 1f), YellowShadow = new(150f / 255, 110f / 255, 10f / 255, 1f);
    private static readonly Color GreyInk = new(110f / 255, 104f / 255, 96f / 255, 1f);

    private enum Phase { Idle, Showing, Closing }
    private static Phase _phase;
    private static object? _token, _decided;
    private static float _shownAt, _closedAt;
    private static bool _rearmed, _forced, _pressed;
    private static RuntimeObject? _holder, _dim, _ring, _card, _button, _face, _buttonLabel, _image, _native;
    private static RuntimeObject? _corner;
    private static int _cornerOrder;
    private static Texture2D? _texture;
    private static string? _bgmLabel;
    private static float _music = float.NaN;
    private static bool _musicOff;

    internal static void Tick(Fight? fight)
    {
        if (!Compatibility.Allowed(Feature)) return;
        try { TickCore(fight, Time.unscaledTime); }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    // The player shrank a full-screen fight ("PIP 모드" or Esc): finished, shown or not.
    internal static void Shrunk(string by)
    {
        if (!Compatibility.Allowed(Feature)) return;
        try
        {
            if (_phase == Phase.Showing) Close(by, true, Time.unscaledTime);
            else Known(by);
        }
        catch (Exception ex) { Compatibility.Block(Feature, ex, Clear); }
    }

    // The player already uses the PiP: the tutorial is not needed.
    internal static void Known(string why)
    {
        if (_rearmed || Plugin.FightPipTutorialDone.Value) return;
        Plugin.FightPipTutorialDone.Value = true;
        Plugin.Diagnostics.State("fightPip.tutorial", "knows " + why);
    }

    // PvP, breakers, scene cleanup: gone at once, the capsule's order and the music back.
    internal static void Clear()
    {
        _phase = Phase.Idle;
        _pressed = false;
        try { RestoreCorner(); } catch { /* A disposed capsule has no order to restore. */ }
        try { Music(0f, force: true); } catch { _musicOff = true; }
        Dispose();
    }

    private static void TickCore(Fight? fight, float now)
    {
        if (DevelopmentBuild && RearmKey()) Rearm(fight);
        switch (_phase)
        {
            case Phase.Showing:
                if (fight is not { } shown || !ReferenceEquals(shown.Token, _token)) { Close("session-end", now - _shownAt >= CountAfter, now); break; }
                if (shown.Closing) { Close("fight-end", now - _shownAt >= CountAfter, now); break; }
                if (!shown.Expanded) { Close("pip", true, now); break; }
                Animate(now);
                Button(now);
                break;
            case Phase.Closing:
                Leave(now);
                break;
            default:
                if (fight is { } candidate) Consider(candidate, now);
                break;
        }
    }

    // Once per fight: shown 0.8 s after the platform opens while everything allows it, given up 3 s after it (unless forced).
    private static void Consider(Fight fight, float now)
    {
        if (ReferenceEquals(fight.Token, _decided) || fight.PlatformAt < 0f) return;
        var since = now - fight.PlatformAt;
        if (since < ShowDelay && !_forced) return;
        var why = Unready(fight);
        if (why == null && (since <= ShowLatest || _forced)) { Show(fight, now); return; }
        if (since <= ShowLatest && !_forced) return;
        _decided = fight.Token;
        if (why != "done") Plugin.Diagnostics.State("fightPip.tutorial", "skip " + (why ?? "late"));
    }

    // Why it cannot show now, or null.
    private static string? Unready(Fight fight)
    {
        if (Plugin.FightPipTutorialDone.Value) return "done";
        if (!fight.Waiting || !fight.Expanded || fight.Closing) return "not waiting full screen";
        if (!_rearmed && !string.IsNullOrEmpty(Plugin.FightPipPlace.Value)) { Known("place"); return "knows place"; }
        if (fight.Corner == null || fight.Corner.Get<bool>("isDisposed") || !fight.Corner.Get<bool>("visible")) return "no capsule";
        var logic = RuntimeObject.StaticField(RuntimeObject.FindClass("GameLogic", "GameLogicManager"), "_inst");
        if (logic?.Get("replay")?.Get("Session")?.Get<bool>("IsReplay") == true) return "replay";
        if (logic?.Get("watch")?.Call("PlayerIsWatcher")?.Value<bool>() == true) return "spectator";
        if (!Application.isFocused) return "unfocused";
        if (ModUi.IsOpen || UpdateNotificationUi.ConsumedInput || PingDetailsUi.Showing) return "covered by mod";
        if (GameUi.FocusWindow()?.Pointer != fight.Window.Pointer) return "covered by " + (GameUi.FocusWindow()?.TypeName ?? "none");
        return null;
    }

    private static void Show(Fight fight, float now)
    {
        Build(fight.Root);
        _corner = fight.Corner;
        _cornerOrder = _corner!.Get<int>("sortingOrder");
        _corner.Set("sortingOrder", Order + 1); // FairyGUI moves it after the tutorial at once.
        _phase = Phase.Showing;
        _token = _decided = fight.Token;
        _shownAt = now;
        _forced = false;
        _pressed = false;
        Animate(now);
        Plugin.Diagnostics.State("fightPip.tutorial", "shown");
    }

    // A dark screen that takes every click under it, the ring around the lit capsule, and the card.
    private static void Build(RuntimeObject root)
    {
        Dispose();
        float width = root.Get<float>("width"), height = root.Get<float>("height");
        _holder = NativeUi.Component(root, width, height);
        _holder.Set("sortingOrder", Order);
        _dim = NativeUi.Component(_holder, width, height);
        _dim.Set("opaque", true);
        SmoothShape.Create(_dim, 0f, 0f, width, height, new Color(0f, 0f, 0f, DimAlpha), 0f, 0f, 0f, 0f);
        _ring = NativeUi.Component(_holder, 10f, 10f);
        _ring.Set("touchable", false);
        _card = NativeUi.Component(_holder, CardW, CardH);
        _card.Set("opaque", true);
        _card.Call("SetXY", CardX, CardY);
        _card.Call("SetPivot", (TailX - CardX) / CardW, 0f, false);
        // The tail: a turned square whose lower half the body covers, the body's ring opened under it.
        var tail = SmoothShape.Create(_card, 0f, 0f, 18f, 18f, NoteUi.Cream, 3f, 3f, 3f, 3f, 2.5f, Black);
        tail.Call("SetPivot", .5f, .5f, true);
        tail.Call("SetXY", TailX - CardX, 0f);
        tail.Set("rotation", 45f);
        var shadow = SmoothShape.Create(_card, 0f, 3f, CardW, CardH, NoteUi.Shadow, 12f, 12f, 12f, 12f);
        _card.Call("SetChildIndex", shadow, 0);
        SmoothShape.Create(_card, 0f, 0f, CardW, CardH, NoteUi.Cream, 12f, 12f, 12f, 12f, 2.5f, Black);
        SmoothShape.Create(_card, TailX - CardX - 8.5f, 0f, 17f, 3f, NoteUi.Cream, 0f, 0f, 0f, 0f);
        Picture(_card);
        SmoothShape.Create(_card, Pad, Pad, ImageW, ImageH, Clear0, 8f, 8f, 8f, 8f, 2.5f, Black);
        var y = Pad + ImageH + 14f;
        NoteUi.Text(_card, ModText.Text("전투 PIP"), Pad, y, ImageW, 30f, 22, NoteUi.Ink);
        NoteUi.Text(_card, ModText.Text("관전하는 전투를 작은 창으로 보면서"), Pad, y + 34f, ImageW, 24f, 17, NoteUi.Ink);
        NoteUi.Text(_card, ModText.Text("필드를 계속 볼 수 있습니다."), Pad, y + 59f, ImageW, 24f, 17, NoteUi.Ink);
        var rows = new[] { ("PIP 모드", "이 버튼 또는 Esc"), ("전체 화면", "작은 창 클릭") };
        var labels = rows.Select((row, n) => NoteUi.Text(_card, ModText.Text(row.Item1), Pad, y + 89f + n * 21f, 200f, 20f, 14, NoteUi.Ink)).ToList();
        var valueX = Pad + labels.Max(label => label.Get<float>("textWidth")) + 10f;
        for (var n = 0; n < rows.Length; n++)
            NoteUi.Text(_card, ModText.Text(rows[n].Item2), valueX, y + 89f + n * 21f, ImageW, 20f, 14, GreyInk);
        _button = NativeUi.Component(_card, ButtonW, ButtonH + 3f);
        _button.Call("SetXY", CardW - Pad - ButtonW, CardH - Pad - ButtonH);
        _button.Set("opaque", true);
        SmoothShape.Create(_button, 0f, 3f, ButtonW, ButtonH, YellowShadow, ButtonH / 2, ButtonH / 2, ButtonH / 2, ButtonH / 2);
        _face = SmoothShape.Create(_button, 0f, 0f, ButtonW, ButtonH, Yellow, ButtonH / 2, ButtonH / 2, ButtonH / 2, ButtonH / 2, 2.5f, Black);
        _buttonLabel = NoteUi.Text(_button, ModText.Text("확인"), 0f, .5f - NoteUi.CentreLift * 18, ButtonW, ButtonH, 18, NoteUi.Ink, align: 1);
    }

    // The preview, decoded once a session and kept while the tutorial may still show.
    private static void Picture(RuntimeObject card)
    {
        if (_texture == null || _texture.m_CachedPtr == IntPtr.Zero)
        {
            var bytes = Convert.FromBase64String(string.Concat(FightPipTutorialImage.Jpeg));
            _texture = new Texture2D(2, 2, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            if (!ImageConversion.LoadImage(_texture, bytes, true)) throw new InvalidOperationException("Tutorial preview unreadable");
        }
        // NTexture(Texture, Texture alphaTexture, float xScale, float yScale), as FightPip builds its picture's.
        _native = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "NTexture"), new RuntimeObject(_texture.Pointer), null!, 1f, 1f);
        SmoothShape.Watch(_native);
        _image = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GImage"));
        _image.Set("touchable", false);
        card.Call("AddChild", _image);
        _image.Set("texture", _native);
        _image.Call("SetSize", ImageW, ImageH);
        _image.Call("SetXY", Pad, Pad);
    }

    private static void Animate(float now)
    {
        var t = now - _shownAt;
        var k = Ease(t / DimIn);
        _dim!.Set("alpha", k);
        Ring(now, k);
        var a = Math.Clamp((t - CardDelay) / CardIn, 0f, 1f);
        var scale = .9f + .1f * Back(a);
        _card!.Call("SetScale", scale, scale);
        _card.Set("alpha", Ease(a));
        Music(k);
    }

    // A yellow ring around the lit capsule, its alpha pulsing.
    private static void Ring(float now, float strength)
    {
        if (_ring == null || _corner == null || _corner.Get<bool>("isDisposed")) return;
        float x = _corner.Get<float>("x"), y = _corner.Get<float>("y"), w = _corner.Get<float>("width"), h = 44f;
        if (_ring.Get<int>("numChildren") == 0)
            SmoothShape.Create(_ring, 0f, 0f, w + 14f, h + 14f, Clear0, h / 2 + 7f, h / 2 + 7f, h / 2 + 7f, h / 2 + 7f, 3.5f, Yellow);
        _ring.Call("SetXY", x - 7f, y - 7f);
        var phase = now % PulsePeriod / PulsePeriod;
        _ring.Set("alpha", strength * (.75f + .25f * (float)Math.Cos(phase * 2 * Math.PI)));
    }

    // 확인: hover lightens it, a press darkens and sinks it, a release over it finishes the tutorial.
    private static void Button(float now)
    {
        if (_button == null || _face == null) return;
        var over = GameUi.PointerPath().Any(item => item.Pointer == _button.Pointer);
        if (UnityEngine.Input.GetMouseButtonDown(0) && over) _pressed = true;
        var release = UnityEngine.Input.GetMouseButtonUp(0);
        if (!UnityEngine.Input.GetMouseButton(0) && !release) _pressed = false;
        var down = _pressed && over;
        SmoothShape.Fill(_face, down ? YellowPress : over ? YellowHover : Yellow);
        var sink = down ? 2f : 0f;
        if (_face.Get<float>("y") != sink) { _face.Call("SetXY", 0f, sink); _buttonLabel!.Call("SetXY", 0f, sink + .5f - NoteUi.CentreLift * 18); }
        if (release)
        {
            var clicked = _pressed && over;
            _pressed = false;
            if (clicked) Close("ok", true, now);
        }
    }

    private static void Close(string by, bool counted, float now)
    {
        if (_phase != Phase.Showing) return;
        _phase = Phase.Closing;
        _closedAt = now;
        _pressed = false;
        RestoreCorner();
        if (_holder != null && !_holder.Get<bool>("isDisposed")) _holder.Set("touchable", false);
        if (counted && !Plugin.FightPipTutorialDone.Value) Plugin.FightPipTutorialDone.Value = true;
        _rearmed = _rearmed && !counted;
        Plugin.Diagnostics.State("fightPip.tutorial", $"closed by={by}; counted={counted}");
    }

    private static void Leave(float now)
    {
        var u = now - _closedAt;
        var gone = _holder == null || _holder.Get<bool>("isDisposed");
        if (!gone)
        {
            var c = Ease(u / CardOut);
            _card!.Set("alpha", Math.Min(_card.Get<float>("alpha"), 1f - c));
            var scale = 1f - .04f * c;
            _card.Call("SetScale", scale, scale);
            var k = 1f - Smooth(u / DimOut);
            _dim!.Set("alpha", Math.Min(_dim.Get<float>("alpha"), k));
            _ring?.Set("alpha", Math.Min(_ring.Get<float>("alpha"), k));
            Music(k);
        }
        if (u < DimOut && !gone) return;
        Music(0f, force: true);
        Dispose();
        _phase = Phase.Idle;
        // Once finished the preview is not needed again this session.
        if (Plugin.FightPipTutorialDone.Value && _texture != null) { UnityEngine.Object.Destroy(_texture); _texture = null; }
    }

    private static void RestoreCorner()
    {
        var corner = _corner;
        _corner = null;
        if (corner == null || corner.Get<bool>("isDisposed")) return;
        if (corner.Get<int>("sortingOrder") != _cornerOrder) corner.Set("sortingOrder", _cornerOrder);
    }

    private static void Dispose()
    {
        if (_native != null) { SmoothShape.Unwatch(_native); _native = null; }
        if (_holder != null && !_holder.Get<bool>("isDisposed")) GameUi.Dispose(_holder);
        _holder = _dim = _ring = _card = _button = _face = _buttonLabel = _image = null;
    }

    // The music turned down to MusicLeft of the player's own BGM volume at duck 1, through the RTPC the game's volume
    // setting writes (LocalStore.BGMVolumeLabel, GameSettings.GetBGMVolume); its own value comes back at 0. The game's Wwise
    // project has no low-pass control to muffle it with (only the four volume RTPCs). A failure only gives up the music.
    private static void Music(float duck, bool force = false)
    {
        if (_musicOff || !force && duck <= 0f && float.IsNaN(_music)) return;
        try
        {
            if (!AkSoundEngine.IsInitialized()) return;
            _bgmLabel ??= RuntimeObject.StaticField(RuntimeObject.FindClass("Core", "LocalStore"), "BGMVolumeLabel")?.String();
            if (string.IsNullOrEmpty(_bgmLabel)) throw new MissingFieldException("LocalStore", "BGMVolumeLabel");
            var own = RuntimeObject.StaticCall(RuntimeObject.FindClass("Core", "GameSettings"), "GetBGMVolume")!.Value<float>();
            var value = own * (1f - (1f - MusicLeft) * duck);
            if (duck <= 0f)
            {
                if (float.IsNaN(_music)) return;
                AkSoundEngine.SetRTPCValue(_bgmLabel, own);
                _music = float.NaN;
                return;
            }
            if (Math.Abs(value - _music) < .001f) return;
            AkSoundEngine.SetRTPCValue(_bgmLabel, value);
            _music = value;
        }
        catch (Exception ex)
        {
            _musicOff = true;
            Plugin.Diagnostics.State("fightPip.tutorial", "music unavailable: " + ex.GetType().Name);
        }
    }

    // Development builds: Ctrl+Shift+F5 forgets that it was finished, ignores what says the player knows the PiP, and shows
    // it during a waiting full-screen fight at once, or at the next one.
    private static bool RearmKey() =>
        UnityEngine.Input.GetKeyDown(KeyCode.F5)
        && (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl))
        && (UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift));

    private static void Rearm(Fight? fight)
    {
        if (_phase != Phase.Idle) return;
        Plugin.FightPipTutorialDone.Value = false;
        _rearmed = true;
        _forced = true;
        _decided = null;
        Plugin.Diagnostics.State("fightPip.tutorial", "rearmed" + (fight is { Waiting: true, Expanded: true } ? "; showing now" : "; next waiting fight"));
    }

    private static float Ease(float t) { t = Math.Clamp(t, 0f, 1f); return 1f - (1f - t) * (1f - t) * (1f - t); }
    private static float Smooth(float t) { t = Math.Clamp(t, 0f, 1f); return t * t * (3f - 2f * t); }

    private static float Back(float t)
    {
        t = Math.Clamp(t, 0f, 1f) - 1f;
        const float s = 1.4f;
        return t * t * ((s + 1f) * t + s) + 1f;
    }
}
