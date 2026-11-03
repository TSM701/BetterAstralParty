using System.Diagnostics;

namespace BetterAstralParty;

// Main-thread stall watch (2026-11-01: a match stopped responding, the game was killed, and the logs could not tell whether
// the mod's tick or the game was stuck; Unity's own log was overwritten by the next two starts). ModUi.Tick marks each of
// its feature segments; a background thread notices when no mark has come for five seconds while the game had focus,
// writes the lines the log still buffers and names the segment the main thread is in ("game" between the mod's ticks),
// and records how long the stall lasted if it ends. Recording only (diagnostics ON); it never touches Unity or IL2CPP.
internal static class StallWatch
{
    private const double Limit = 5;
    internal const int ModStart = -2; // ModUi.Tick before its measured feature segments (updates, focus, PvP safety).
    private static long _beat;
    private static volatile int _segment = -1;
    private static volatile bool _focused;
    private static Thread? _thread;

    // The main thread is now in DiagnosticLog.TickFeatures[segment], or between the mod's ticks when segment is -1.
    internal static void Enter(int segment)
    {
        _segment = segment;
        Volatile.Write(ref _beat, Stopwatch.GetTimestamp());
    }

    internal static void Focus(bool focused) => _focused = focused;

    internal static string Segment(int segment) => segment == ModStart ? "modStart"
        : segment >= 0 && segment < DiagnosticLog.TickFeatures.Length ? DiagnosticLog.TickFeatures[segment] : "game";

    internal static void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "BAP stall watch", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    private static void Run()
    {
        long reported = 0;
        var segment = -1;
        while (true)
        {
            Thread.Sleep(1000);
            try
            {
                var beat = Volatile.Read(ref _beat);
                if (reported != 0 && beat != reported)
                {
                    Plugin.Diagnostics.Write(FormattableString.Invariant(
                        $"main.stall ended after {(beat - reported) / (double)Stopwatch.Frequency:F1}s; inside={Segment(segment)}"));
                    reported = 0;
                }
                if (reported != 0 || beat == 0 || !_focused || !Plugin.Diagnostics.IsRecording) continue;
                var seconds = (Stopwatch.GetTimestamp() - beat) / (double)Stopwatch.Frequency;
                if (seconds < Limit) continue;
                segment = _segment;
                reported = beat;
                Plugin.Diagnostics.Write(FormattableString.Invariant($"main.stall silent={seconds:F1}s; inside={Segment(segment)}"));
                Plugin.Diagnostics.Flush();
            }
            catch (Exception ex)
            {
                // A failing watch stops quietly; it must never disturb the game.
                Plugin.Logger.LogWarning("[stall watch] " + ex.GetType().Name);
                return;
            }
        }
    }
}
