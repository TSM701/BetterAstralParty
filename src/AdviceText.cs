using System.Security;
using System.Text.RegularExpressions;

namespace BetterAstralParty;

// Presentation only: calculation results and their uncertainty remain unchanged.
internal static class AdviceText
{
    internal static string Quick(string text)
    {
        // Tokenize plain text, not encoded entities: styling the 183 in &#183; breaks parsing.
        text = ModText.Text(text);
        var result = new System.Text.StringBuilder();
        var cursor = 0;
        foreach (Match match in Regex.Matches(text, @"-?[0-9]+(?:[.,][0-9]+)?(?:~[0-9]+(?:[.,][0-9]+)?)?%?"))
        {
            result.Append(SecurityElement.Escape(text[cursor..match.Index]));
            var warning = match.Value.EndsWith('%') && match.Value != "0%";
            result.Append(warning ? $"<font color='#B5163B'>{match.Value}</font>" : match.Value);
            cursor = match.Index + match.Length;
        }
        result.Append(SecurityElement.Escape(text[cursor..]));
        return result.ToString();
    }

    internal static string CardTitle(string title) => title.TrimEnd('*') switch
    {
        "효과 있음" => "이득",
        "조건부 이득" => "조건부",
        "효과 없음 · 보존" => "보존",
        "더 싼 카드 우선" => "저비용 우선",
        var other => other
    };

    internal const float HoverSlide = 8f;

    // Keep the entrance intact; fade out during the last half-second.
    internal const float DamageLifetime = 3f;
    internal static bool FinalDamageReady(bool sawArithmetic, bool arithmeticExited,
        bool attackUpdated, bool defenseUpdated, bool escaped) =>
        escaped || arithmeticExited || sawArithmetic && attackUpdated && defenseUpdated;
    internal static float DamageAlpha(float elapsed, bool lethal = false) =>
        elapsed < 0.5f ? (lethal ? 1f : Reveal(elapsed, 0.12f)) : 1 - Reveal(elapsed - (DamageLifetime - 0.5f), 0.5f);

    // Anchor is inside the panel: clear the whole panel, including the entrance slide.
    internal static float HoverAbovePanel(float anchorY, float height, float scale) =>
        -anchorY - height * scale - HoverSlide - 12f;

    internal static float Reveal(float elapsed, float duration = 0.16f)
    {
        var t = Math.Clamp(elapsed / duration, 0f, 1f);
        return 1 - (1 - t) * (1 - t);
    }

    // Relic cutIn scale keys: .5 → 1.1 (11 frames), → .95 (9), → 1 (10), at 60 fps.
    // Skip the original 0.4s entrance delay: combat recommendations must be immediate.
    internal static float RecommendationScale(float elapsed)
    {
        static float Ease(float x) => 1 - (1 - x) * (1 - x);
        if (elapsed < 0) return 0.5f;
        if (elapsed < 11f / 60) return 0.5f + 0.6f * Ease(elapsed / (11f / 60));
        if (elapsed < 20f / 60) return 1.1f - 0.15f * Ease((elapsed - 11f / 60) / (9f / 60));
        if (elapsed < 0.5f) return 0.95f + 0.05f * Ease((elapsed - 20f / 60) / (10f / 60));
        return 1f;
    }
    private static readonly Regex Tokens = new(
        @"방어 추천|회피 추천|효과 있음|조건부 이득|효과 없음 · 보존|더 싼 카드 우선|판단 보류|사용 불가|회피 불가|평균 체력 손실|평균 피해|전투불능|추가 피해|피해 감소|비용|방어(?=\*| ·| 전투)|회피(?=\*| 전투)|-?[0-9]+(?:[.,][0-9]+)?(?:~[0-9]+(?:[.,][0-9]+)?)?%?");

    internal static string Format(string plain, int size, string? attack = null, string? defense = null, string? hp = null)
    {
        // Escape each plain segment once, before inserting our own markup.
        return string.Join("\n", plain.Replace("★ ", "").Split('\n').Select(line =>
        {
            var numeric = line.Contains("피해") || line.Contains("손실") || line.Contains("전투불능") || line.Contains("비용");
            var cursor = 0;
            var result = new System.Text.StringBuilder();
            foreach (Match match in Tokens.Matches(line))
            {
                result.Append(SecurityElement.Escape(ModText.Text(line[cursor..match.Index])));
                var word = match.Value;
                var icon = word switch
                {
                    "방어 추천" or "방어" or "피해 감소" => Icon(defense, "◇", size),
                    "추가 피해" => Icon(attack, "◆", size),
                    "평균 체력 손실" or "평균 피해" or "전투불능" => Icon(hp, "♥", size),
                    "회피 추천" or "회피" or "회피 불가" => "↗ ",
                    "비용" => "◆ ",
                    _ => ""
                };
                var color = word switch
                {
                    "방어 추천" or "회피 추천" or "효과 있음" => "17663C",
                    "조건부 이득" or "효과 없음 · 보존" or "더 싼 카드 우선" or "판단 보류" => "795000",
                    "사용 불가" or "회피 불가" => "B5163B",
                    _ => numeric && (char.IsDigit(word[0]) || word[0] == '-') ? "005B83" : null
                };
                result.Append(icon);
                var escaped = SecurityElement.Escape(ModText.Text(word));
                result.Append(color == null ? escaped : $"<font color='#{color}'>{escaped}</font>");
                cursor = match.Index + match.Length;
            }
            result.Append(SecurityElement.Escape(ModText.Text(line[cursor..])));
            return result.ToString();
        }));
    }

    private static string Icon(string? url, string fallback, int size) =>
        url != null && Regex.IsMatch(url, @"\Aui://[A-Za-z0-9_/]+\z")
            ? $"<img src='{url}' width='{Math.Clamp(size, 14, 48)}' height='{Math.Clamp(size, 14, 48)}'/> "
            : fallback + " ";
}
