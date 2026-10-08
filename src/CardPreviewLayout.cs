namespace BetterAstralParty;

internal static class CardPreviewLayout
{
    internal const float Width = 440, Height = 644;

    internal static (float X, float Y, float Scale) Place(float width, float height, float x, float y, float desired,
        float contentWidth = Width, float contentHeight = Height)
    {
        var scale = Math.Max(0.01f, Math.Min(desired, Math.Min((width - 32) / contentWidth, (height - 32) / contentHeight)));
        var w = contentWidth * scale;
        var h = contentHeight * scale;
        // Prefer beside the pointer, never over its hit target; flip at the right edge.
        var left = x + 32 + w <= width - 16 ? x + 32 : x - 32 - w;
        return (Math.Clamp(left, 16, Math.Max(16, width - w - 16)),
            Math.Clamp(y - h / 2, 16, Math.Max(16, height - h - 16)), scale);
    }

    internal static (bool Relic, int Id) Resolve(Dictionary<string, int> cards, Dictionary<string, int> relics, string text)
    {
        var card = cards.TryGetValue(text, out var cardId);
        var relic = relics.TryGetValue(text, out var relicId);
        // Exact native messages only; even cross-kind name collisions must not guess an item.
        return card == relic ? default : relic ? (true, relicId) : (false, cardId);
    }

    internal static void AddUnique(Dictionary<string, int> map, string text, int id)
    {
        if (string.IsNullOrWhiteSpace(text) || id <= 0) return;
        // 0 marks ambiguous labels. Never pick an arbitrary card with the same translated name.
        if (map.TryGetValue(text, out var old) && old != id) map[text] = 0;
        else map[text] = id;
    }
}
