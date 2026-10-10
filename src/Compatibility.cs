namespace BetterAstralParty;

// Session-only circuit breakers. Saved user preferences are never overwritten.
internal static class Compatibility
{
    private static readonly Dictionary<string, string> Blocked = new();
    internal static int Revision { get; private set; }
    internal static string? Reason(string feature)
    {
        if (Blocked.TryGetValue(feature, out var reason)) return ModText.Text(reason);
        if (feature is "Details" or "KoMinimum") return Reason("Enabled");
        if (feature is not ("MuteUnfocused" or "MatchFocus" or "Diagnostics" or "Notices")
            && Blocked.TryGetValue("CoreUi", out reason)) return reason;
        return null;
    }
    internal static bool Allowed(string feature) => Reason(feature) == null;
    internal static void Block(string feature, Exception error, params Action[] cleanup)
    {
        var reason = error switch
        {
            MissingFieldException => "게임 필드 변경 · 로그 확인",
            MissingMethodException => "게임 함수 변경 · 로그 확인",
            InvalidCastException => "게임 API 타입 변경 · 로그 확인",
            TypeLoadException => "필수 타입·UI 없음 · 로그 확인",
            _ => "호환성 오류 · 재시작 필요"
        };
        if (!Blocked.TryAdd(feature, reason)) return;
        Revision++;
        Plugin.Logger.LogWarning($"[Compatibility] blocked={feature}; {error}");
        Plugin.Diagnostics.Error("Compatibility." + feature, error);
        foreach (var action in cleanup)
        {
            try { action(); }
            catch (Exception ex) { Plugin.Logger.LogWarning($"[Compatibility] cleanup={feature}; {ex}"); }
        }
    }
}
