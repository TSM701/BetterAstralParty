using UnityEngine;

namespace BetterAstralParty;

// Presentation only: do not invoke native selection, ready, swap or network actions.
internal static class CharacterGuideUi
{
    private sealed record Badge(RuntimeObject Owner, RuntimeObject Panel, RuntimeObject Text, RuntimeObject Image, RuntimeObject? Mask);
    private static readonly Dictionary<IntPtr, Badge> Badges = new();
    private static readonly HashSet<IntPtr> Seen = new();
    private static readonly List<RuntimeObject> Players = new();
    private static readonly Dictionary<IntPtr, (RuntimeObject Container, float Original, float Applied)> SwapPositions = new();
    private static readonly Dictionary<IntPtr, (IntPtr Hero, IntPtr Skin, int Chosen, int Language)> ReadyStates = new();
    private static RuntimeObject? _room, _popup, _content, _cardTemplate;
    private static RuntimeObject? _heroLoad;
    private static bool _heroBound;
    private static IntPtr _hover;
    private static float _scanAt, _hoverAt, _refreshAt, _scroll, _overflow;
    private static string _descriptionKey = "";
    private static IntPtr _config, _helper;

    internal static void Tick()
    {
        if (!Compatibility.Allowed("CharacterGuide")) return;
        try
        {
            var root = GameUi.Root;
            if (root == null) return;
            if (_heroLoad != null && _heroLoad.Get<bool>("IsCompleted"))
            {
                var load = _heroLoad; _heroLoad = null;
                if (load.Call("GetResult") == null) throw new InvalidOperationException("Hero UI package load failed");
                _scanAt = 0; // Consume newly available UI resources this frame.
            }
            if (Time.unscaledTime >= _scanAt)
            {
                _scanAt = Time.unscaledTime + 0.2f;
                Scan(root);
            }
            RefreshReadyBadges();
            Hover(root);
        }
        catch (Exception ex) { Compatibility.Block("CharacterGuide", ex, Clear); }
    }

    private static RuntimeObject? Config(int id)
    {
        if (_config == IntPtr.Zero) _config = RuntimeObject.FindClass("", "StaticConfigure");
        if (_config == IntPtr.Zero) return null;
        var table = RuntimeObject.StaticCall(_config, "get_Character")?.Get("InfoDict");
        return table != null && table.Call("ContainsKey", id)!.Value<bool>() ? table.Call("get_Item", id) : null;
    }

    private static bool HeroUiReady()
    {
        if (_heroLoad != null) return false;
        var package = RuntimeObject.FindClass("FairyGUI", "UIPackage");
        if (RuntimeObject.StaticCall(package, "GetByName", "Hero") == null)
        {
            _heroLoad = RuntimeObject.StaticCall(package, "AddPackageAsync", "Hero", "")!.Call("GetAwaiter")
                ?? throw new InvalidOperationException("Hero UI load awaiter unavailable");
            return false;
        }
        if (!_heroBound)
        {
            RuntimeObject.StaticCall(RuntimeObject.FindClass("UI", "UIHeroPanel"), "BindAll");
            _heroBound = true;
        }
        return true;
    }

    private static void Scan(RuntimeObject root)
    {
        Seen.Clear();
        var hero = GameUi.Find(root, "UIHeroPanel");
        var list = hero?.Field("com_HeroList")?.Field("list_Hero");
        if (list != null)
            for (var i = 0; i < list.Get<int>("numChildren"); i++)
            {
                var item = list.Call("GetChildAt", i)!;
                if (item.TypeName != "UIHero_Button_Hero" || !GameUi.Visible(item)) continue;
                AddBadge(item, Config(item.Field("HeroId")!.Value<int>())?.Get<int>("TagType") ?? 0, false);
            }
        var room = GameUi.Find(root, "UIRoomHeroPanel");
        if (_room?.Pointer != room?.Pointer)
        {
            RestoreSwapPositions();
            Players.Clear(); ReadyStates.Clear(); _room = room; Hide();
            GameUi.Dispose(_popup); _popup = null; _content = null; _descriptionKey = "";
        }
        if (room != null)
            for (var slot = 1; slot <= 4; slot++)
            {
                var player = room.Field("com_player_" + slot)!;
                if (!VisibleCombatAdvisor.IsPve(player.Field("mapType")!.Value<int>())) continue;
                AlignSwapButton(player, root);
                if (!Players.Any(p => p.Pointer == player.Pointer)) Players.Add(player);
                // Prepare resources before the first selection, without blocking the game thread.
                if (_cardTemplate == null && HeroUiReady())
                    _cardTemplate = NativeUi.Create("Hero", "Hero_Button_Hero");
                if (player.Field("isChoose")!.Get<int>("selectedIndex") == 0) continue;
                var config = player.Field("selectHero")?.Get("InfoConfig");
                if (config != null) AddBadge(player, config.Get<int>("TagType"), true);
            }
        foreach (var pair in Badges.ToArray())
            if (!Seen.Contains(pair.Key)) { GameUi.Dispose(pair.Value.Panel); Badges.Remove(pair.Key); }
    }

    private static void RefreshReadyBadges()
    {
        // Four known preparation slots only; no per-frame tree scan or unchanged badge rebuild.
        foreach (var player in Players)
        {
            if (!GameUi.Visible(player)) continue;
            var hero = player.Field("selectHero");
            var chosen = player.Field("isChoose")!.Get<int>("selectedIndex");
            var state = (hero?.Pointer ?? IntPtr.Zero, hero?.Get("standingPainting")?.Pointer ?? IntPtr.Zero,
                chosen, ModText.Revision);
            if (ReadyStates.TryGetValue(player.Pointer, out var previous) && previous == state) continue;
            ReadyStates.Remove(player.Pointer);
            // Hide old role and texture together, including unknown/new roles and deselection.
            if (Badges.Remove(player.Pointer, out var old)) GameUi.Dispose(old.Panel);
            if (_hover == player.Pointer) { Hide(); _descriptionKey = ""; }
            if (chosen != 0)
            {
                var config = hero?.Get("InfoConfig");
                if (config == null || !AddBadge(player, config.Get<int>("TagType"), true)) continue;
            }
            // A deferred first render must remain pending, not wait for another selection/scan.
            ReadyStates[player.Pointer] = state;
        }
    }

    private static bool AddBadge(RuntimeObject owner, int tag, bool ready)
    {
        var text = CharacterRole.Label(tag, ModText.Korean);
        if (text.Length == 0) return true; // Unknown roles intentionally have no label.
        if (!HeroUiReady()) return false;
        var parent = ready ? owner : owner.Field("com_Loader")!;
        var portrait = parent.Field("loader_Character")!;
        var quality = parent.Field("com_Qulity");
        if (quality == null) return false;
        var root = GameUi.Root!;
        var top = parent.Call("RootToLocal", quality.Call("LocalToRoot", Vector2.zero, root)!.Value<Vector2>(), root)!.Value<Vector2>();
        var end = parent.Call("RootToLocal", quality.Call("LocalToRoot",
            new Vector2(quality.Get<float>("width"), quality.Get<float>("height")), root)!.Value<Vector2>(), root)!.Value<Vector2>();
        var width = portrait.Get<float>("width") * portrait.Get<float>("scaleX");
        var left = portrait.Get<float>("x");
        var textWidth = end.x - top.x;
        var labelHeight = Math.Max(28, (end.y - top.y) * 1.6f);
        var bleed = ready ? 0f : labelHeight * 0.25f;
        width += bleed * 2;
        left -= bleed;
        var lift = labelHeight * 0.15f;
        var height = labelHeight + lift + end.y - top.y;
        if (ready)
        {
            // One UI unit of right-only overlap covers the thin edge seam; keep left/text anchors.
            width += 1f;
            // Keep portrait width; extend only downward underneath the profile plate.
            var profile = owner.Field("com_PlayerLabel")!;
            var profileMiddle = parent.Call("RootToLocal", profile.Call("LocalToRoot",
                new Vector2(0f, profile.Get<float>("height") * 0.5f), root)!.Value<Vector2>(), root)!.Value<Vector2>();
            height = Math.Max(end.y, profileMiddle.y) - (top.y - labelHeight - lift);
        }
        if (width <= 0) return false;
        var frameOwner = owner;
        if (ready)
        {
            _cardTemplate ??= NativeUi.Create("Hero", "Hero_Button_Hero");
            var hero = owner.Field("selectHero")!;
            _cardTemplate!.Field("breakthrough")!.Set("selectedIndex", hero.Get<bool>("isBreakThrough") ? 1 : 0);
            _cardTemplate.Field("Collaborate")!.Set("selectedIndex", hero.Get("InfoConfig")!.Get<bool>("IsLinkage") ? 1 : 0);
            frameOwner = _cardTemplate;
        }
        RuntimeObject? frame = null;
        for (var i = 0; i < frameOwner.Get<int>("numChildren"); i++)
        {
            var child = frameOwner.Call("GetChildAt", i)!;
            if (child.Get("asImage") != null && child.Get<bool>("internalVisible") && child.Get<bool>("internalVisible2")
                && child.Get<float>("height") >= frameOwner.Get<float>("height") * 0.8f) frame = child;
        }
        if (frame == null || frame.Get("texture") == null) return false;
        Seen.Add(owner.Pointer);
        if (!Badges.TryGetValue(owner.Pointer, out var badge))
        {
            var panel = NativeUi.Component(parent, width, height);
            panel.Set("touchable", false);
            panel.Call("SetupOverflow", 1);
            var image = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GImage"));
            image.Set("touchable", false); panel.Call("AddChild", image);
            var label = NativeUi.Label(panel, "", 4, 0, width - 8, labelHeight, 19, dark: true);
            label.Set("autoSize", 3);
            RuntimeObject? mask = null;
            if (ready)
            {
                // Mask only our private strip; native YOU, grade and character art stay untouched.
                mask = RuntimeObject.New(RuntimeObject.FindClass("FairyGUI", "GGraph"));
                mask.Set("touchable", false);
                panel.Call("AddChild", mask);
                panel.Set("mask", mask.Get("displayObject")!);
            }
            badge = new(owner, panel, label, image, mask); Badges[owner.Pointer] = badge;
        }
        // The strip overdraws underneath native trim; never reorder native children.
        var gradeIndex = parent.Call("GetChildIndex", quality)!.Value<int>();
        if (ready)
        {
            gradeIndex = Math.Min(gradeIndex, parent.Call("GetChildIndex", owner.Field("YOU")!)!.Value<int>());
            gradeIndex = Math.Min(gradeIndex, parent.Call("GetChildIndex", owner.Field("com_PlayerLabel")!)!.Value<int>());
        }
        var portraitIndex = parent.Call("GetChildIndex", portrait)!.Value<int>();
        for (var i = portraitIndex + 1; i < gradeIndex; i++)
        {
            var trim = parent.Call("GetChildAt", i)!;
            if (trim.Get("asImage") != null && trim.Get<float>("height") >= portrait.Get<float>("height") * 0.8f)
            { gradeIndex = i; break; }
        }
        var panelIndex = parent.Call("GetChildIndex", badge.Panel)!.Value<int>();
        parent.Call("SetChildIndex", badge.Panel, gradeIndex - (panelIndex < gradeIndex ? 1 : 0));
        badge.Panel.Call("SetXY", left, top.y - labelHeight - lift);
        badge.Panel.Call("SetSize", width, height);
        if (badge.Mask is { } rounded && (rounded.Get<float>("width") != width || rounded.Get<float>("height") != height))
        {
            rounded.Call("SetSize", width, height);
            var radius = Math.Min(height * 0.3f, width * 0.08f);
            rounded.Get("shape")!.Call("DrawRoundRect", 0f, Color.white, Color.white, radius, radius, 0f, 0f);
        }
        // Centre on the portrait independently of grade artwork and seam bleed.
        var textLeft = ready ? (portrait.Get<float>("width") * portrait.Get<float>("scaleX") - textWidth) / 2f
            : (width - textWidth) / 2f;
        badge.Text.Call("SetXY", textLeft + 4, 0f);
        badge.Text.Call("SetSize", textWidth - 8, labelHeight);
        // Crop the solid lower centre of the native frame, retaining its actual texture/colour.
        badge.Image.Set("texture", frame.Get("texture")!);
        badge.Image.Set("color", frame.Get<Color>("color"));
        var fw = width / 0.65f;
        // Stretch a narrow solid band, rather than the transparent bottom edge of the frame.
        var fh = height / 0.02f;
        badge.Image.Call("SetSize", fw, fh);
        badge.Image.Call("SetXY", (width - fw) / 2, -fh * 0.92f);
        if (badge.Text.Get("text")?.String() != text) badge.Text.Set("text", text);
        return true;
    }

    private static void AlignSwapButton(RuntimeObject player, RuntimeObject root)
    {
        var container = player.Field("com_ChangeSlot")!;
        var button = container.Field("btn_ChangeSlot")!;
        var portrait = player.Field("loader_Character")!;
        var buttonLeft = player.Call("RootToLocal", button.Call("LocalToRoot", Vector2.zero, root)!.Value<Vector2>(), root)!.Value<Vector2>().x;
        var cardLeft = player.Call("RootToLocal", portrait.Call("LocalToRoot", Vector2.zero, root)!.Value<Vector2>(), root)!.Value<Vector2>().x;
        var x = container.Get<float>("x");
        var target = x + cardLeft - buttonLeft;
        if (Math.Abs(target - x) < 0.01f) return;
        var original = SwapPositions.TryGetValue(container.Pointer, out var saved) ? saved.Original : x;
        SwapPositions[container.Pointer] = (container, original, target);
        // Move the native container with its hit areas and pending/accept controls. No event replacement.
        container.Set("x", target);
    }

    private static void RestoreSwapPositions()
    {
        foreach (var edit in SwapPositions.Values)
            if (!edit.Container.Get<bool>("isDisposed") && Math.Abs(edit.Container.Get<float>("x") - edit.Applied) < 0.01f)
                edit.Container.Set("x", edit.Original);
        SwapPositions.Clear();
    }

    private static bool Contains(RuntimeObject item, Vector2 mouse, RuntimeObject root)
    {
        var p = item.Call("RootToLocal", mouse, root)!.Value<Vector2>();
        return p.x >= 0 && p.y >= 0 && p.x < item.Get<float>("width") && p.y < item.Get<float>("height");
    }

    private static void Hover(RuntimeObject root)
    {
        if (_room == null || !GameUi.Visible(_room) || ModUi.IsOpen || !Application.isFocused) { Hide(); return; }
        var path = GameUi.PointerPath();
        // Native hit ancestry follows the moved controls, including their nested icons and accept buttons.
        if (path.Any(p => p.TypeName == "UIRoomHero_Com_ChangeSlot")) { Hide(); return; }
        var overPopup = path.Any(p => p.Pointer == _popup?.Pointer);
        if (!overPopup && !path.Any(p => p.Pointer == _room.Pointer)) { Hide(); return; }
        var mouse = new Vector2(Input.mousePosition.x / Math.Max(1, Screen.width) * root.Get<float>("width"),
            (1 - Input.mousePosition.y / Math.Max(1, Screen.height)) * root.Get<float>("height"));
        RuntimeObject? hovered = overPopup ? Players.FirstOrDefault(p => p.Pointer == _hover) : null;
        if (!overPopup)
            foreach (var p in Players)
                if (GameUi.Visible(p) && p.Field("isChoose")!.Get<int>("selectedIndex") != 0
                    && Contains(p.Field("loader_Character")!, mouse, root)) { hovered = p; break; }
        if (hovered == null) { Hide(); return; }
        if (_hover != hovered.Pointer) { Hide(); _hover = hovered.Pointer; _hoverAt = Time.unscaledTime; _refreshAt = 0; }
        var elapsed = Time.unscaledTime - _hoverAt;
        if (elapsed < 0.2f) return;
        if (!HeroUiReady()) return;
        var config = hovered.Field("selectHero")?.Get("InfoConfig");
        if (config == null) { Hide(); return; }
        if (_popup == null || Time.unscaledTime >= _refreshAt)
        {
        _refreshAt = Time.unscaledTime + 0.2f;
        var source = hovered.Field("btn_DetailInfo")!;
        var rows = source.Field("list_Skill")!;
        var names = new List<(string Name, string Description, int Type, string Cooldown)>();
        var skills = rows.Field("itemRenderer")?.Get("Target")?.Field("skillInfos");
        for (var i = 0; i < rows.Get<int>("numChildren"); i++)
        {
            var row = rows.Call("GetChildAt", i)!;
            if (row.TypeName != "UIRoomHero_Com_Skill") continue;
            var name = row.Field("txt_SkillName")!;
            var vars = name.Get("templateVars");
            var title = vars != null && vars.Call("ContainsKey", "skillName")!.Value<bool>()
                ? vars.Call("get_Item", "skillName")!.String() : name.Get("text")!.String();
            var cooldown = skills != null && i < skills.Get<int>("Count")
                ? skills.Call("get_Item", i)!.Get<int>("Round").ToString() : "";
            names.Add((title, row.Field("txt_SkillDesc")!.Get("text")!.String(), row.Field("skillType")!.Get<int>("selectedIndex"), cooldown));
        }
        if (names.Count == 0) return;
        // Read public potential definitions, not the local account's unlock state for another player.
        var breaks = config.Get("PveBreak")!;
        if (_config == IntPtr.Zero) _config = RuntimeObject.FindClass("", "StaticConfigure");
        var table = RuntimeObject.StaticCall(_config, "get_PVENurturance")!.Get("BreakDict")!;
        if (_helper == IntPtr.Zero) _helper = RuntimeObject.FindClass("UI", "UIHelper");
        for (var i = 0; i < breaks.Get<int>("Count"); i++)
        {
            var id = breaks.Call("get_Item", i)!.Value<int>();
            if (!table.Call("ContainsKey", id)!.Value<bool>()) continue;
            var potential = table.Call("get_Item", id)!;
            var desc = RuntimeObject.StaticCall(_helper, "GetLocal", potential.Get<int>("DescriptionID"), 43)!.String();
            var characterName = RuntimeObject.StaticCall(_helper, "GetLocal", config.Get<int>("NameID"), 10)!.String();
            names.Add((characterName, desc, 2, ""));
        }
        var key = string.Join("\n", names) + ModText.Revision;
        if (_popup == null || key != _descriptionKey) Build(root, names, key);
        }
        var reveal = AdviceText.Reveal(elapsed - 0.2f);
        var scale = Math.Min(1f, Math.Min(root.Get<float>("width") / 1920f, root.Get<float>("height") / 1080f));
        var anchor = hovered.Call("LocalToRoot", Vector2.zero, root)!.Value<Vector2>();
        var x = anchor.x + hovered.Get<float>("width") * scale + 12;
        if (x + 620 * scale > root.Get<float>("width") - 12) x = anchor.x - 620 * scale - 12;
        _popup!.Call("SetScale", scale, scale);
        _popup.Call("SetXY", Math.Clamp(x, 12, Math.Max(12, root.Get<float>("width") - 620 * scale - 12)), 80 * scale + (1 - reveal) * 8);
        _popup.Set("visible", true); _popup.Set("alpha", reveal);
        if (overPopup) _scroll = Math.Clamp(_scroll - Input.mouseScrollDelta.y * 48, 0, _overflow);
        _content!.Call("SetXY", 0f, -_scroll);
    }

    private static void Build(RuntimeObject root, List<(string Name, string Description, int Type, string Cooldown)> rows, string key)
    {
        GameUi.Dispose(_popup);
        _popup = NativeUi.Component(root, 620, 650); _popup.Set("sortingOrder", 29000);
        _popup.Set("opaque", true);
        var surface = new NativeUi.Surface(_popup, 620, 650, fixedOpacity: true);
        surface.Graph.Set("color", new Color(0.16f, 0.18f, 0.22f));
        var view = NativeUi.Component(_popup, 588, 618); view.Call("SetXY", 16f, 16f); view.Call("SetupOverflow", 1);
        _content = NativeUi.Component(view, 588, 618);
        var y = 0f;
        foreach (var entry in rows)
        {
            // Same text resolver as the native character tab, without installing click handlers.
            // Skill rows may already be resolved; raw potential definitions are not.
            var description = entry.Description.Contains("[Astral=", StringComparison.Ordinal)
                ? RuntimeObject.StaticCall(RuntimeObject.FindClass("UI", "CommonUIManager"),
                    "MatchSkillTextContent", entry.Description, 0)!.String()
                : entry.Description;
            var potential = entry.Type == 2;
            var row = NativeUi.Create("Hero", potential ? "Hero_Com_FileBreakThrough" : "Hero_Com_Skill")!;
            _content.Call("AddChild", row); row.Call("SetXY", 0f, y); row.Set("touchable", false);
            var scale = 588f / row.Get<float>("width");
            row.Call("SetScale", scale, scale);
            RuntimeObject desc;
            float bottom;
            if (potential)
            {
                var language = RuntimeObject.StaticField(RuntimeObject.FindClass("Core", "GameSettings"), "languageType")!.Value<int>();
                row.Field("language")!.Set("selectedIndex", language == 10 ? 1 : 0);
                var name = row.Field("txt_DescTitle")!;
                name.Call("SetVar", "name", entry.Name); name.Call("FlushVars");
                desc = row.Field("com_Desc")!.Field("title")!;
                // Retain native description artwork, not unlock controls or their state claims.
                for (var i = 0; i < row.Get<int>("numChildren"); i++)
                {
                    var child = row.Call("GetChildAt", i)!;
                    if (i is 0 or 1 or 2 or 3)
                    {
                        child.Set("group", null!);
                    }
                    else child.Set("visible", false);
                }
                desc.Set("text", description);
                NativeUi.OutlineTree(row);
                bottom = row.Field("com_Desc")!.Get<float>("y") + desc.Get<float>("textHeight") + 24;
                row.Call("GetChildAt", 0)!.Call("SetSize", row.Get<float>("width"), bottom);
            }
            else
            {
                row.Field("skillType")!.Set("selectedIndex", entry.Type);
                var name = row.Field("txt_SkillName")!;
                name.Call("SetVar", "skillName", entry.Name); name.Call("FlushVars");
                row.Field("txt_CD")!.Set("text", entry.Cooldown);
                desc = row.Field("txt_SkillDesc")!;
                desc.Set("text", description);
                NativeUi.OutlineTree(row);
                bottom = desc.Get<float>("y") + desc.Get<float>("textHeight") + 24;
            }
            desc.Set("autoSize", 2); desc.Set("singleLine", false);
            // Preserve the native fonts, colours, header strips, icons and skill entrance transition.
            y += bottom * scale + 12;
        }
        _content.Call("SetSize", 588f, y);
        _overflow = Math.Max(0, y - 618); _scroll = 0; _descriptionKey = key;
    }

    private static void Hide()
    {
        if (_popup != null && !_popup.Get<bool>("isDisposed")) _popup.Set("visible", false);
        _hover = IntPtr.Zero;
    }

    internal static void Clear()
    {
        RestoreSwapPositions();
        GameUi.Dispose(_cardTemplate); _cardTemplate = null;
        GameUi.Dispose(_popup); _popup = null; _content = null;
        foreach (var badge in Badges.Values) GameUi.Dispose(badge.Panel);
        Badges.Clear(); Players.Clear(); ReadyStates.Clear(); _room = null; _hover = IntPtr.Zero; _descriptionKey = "";
    }
}
