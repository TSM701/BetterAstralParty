using UnityEngine;

namespace BetterAstralParty;

internal static class FieldNameFont
{
    // Reuse the live sidebar font without sharing its mutable TextFormat or changing global fonts.
    internal static bool Apply(RuntimeObject label, RuntimeObject? battleUi)
    {
        try
        {
            var source = battleUi?.Field("com_BattlePlayer")?.Field("com_Container")?
                .Field("com_Player_1")?.Field("com_player")?.Field("txt_PlayerName")?.Get("textFormat");
            var font = source?.Field("font")?.String();
            if (string.IsNullOrEmpty(font)) return false;
            var format = label.Get("textFormat")!;
            format.SetField("font", font);
            format.SetField("bold", source!.Field("bold")!.Value<bool>());
            format.SetField("italic", source.Field("italic")!.Value<bool>());
            format.SetField("faceDilate", source.Field("faceDilate")!.Value<float>());
            format.SetField("outline", source.Field("outline")!.Value<float>());
            format.SetField("outlineSoftness", source.Field("outlineSoftness")!.Value<float>());
            format.SetField("outlineColor", Color.black);
            label.Set("textFormat", format);
            Plugin.Diagnostics.State("field.nicknameFont", font);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Diagnostics.Error("FieldNickname.font", ex);
            return false; // Retry on the next scan without dropping HP/buff indicators.
        }
    }
}
