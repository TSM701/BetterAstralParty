using BetterAstralParty;

// Keep Replays (2026-11-18): the first pass of a run puts back replays known before and now gone (the game's 캐시 정리 empties
// its Temp folder and quits), one replay gone during a run is the player's delete and takes its copy, and several gone at once
// keep their copies without coming back.
static class ReplayKeeperCheck
{
    internal static void Run()
    {
        void Check(bool value, string name) { if (!value) throw new Exception("Keep Replays regression: " + name); }
        HashSet<string> Set(params string[] ids) => new(ids, StringComparer.Ordinal);

        var wiped = ReplayKeeperPlan.Decide(Set(), Set("a", "b", "c"), Set("a", "b"), null);
        Check(wiped.Restore.SequenceEqual(new[] { "a", "b" }) && wiped.DropCopy.Count == 0 && wiped.Known.SetEquals(Set("a", "b")),
            "first pass puts known replays back from their copies");
        var noCopy = ReplayKeeperPlan.Decide(Set("a"), Set(), Set("a", "b"), null);
        Check(noCopy.Restore.Count == 0 && noCopy.Known.SetEquals(Set("a")), "a known replay without a copy is forgotten");

        var deleted = ReplayKeeperPlan.Decide(Set("a", "c"), Set("a", "b", "c"), Set(), Set("a", "b", "c"));
        Check(deleted.DropCopy.SequenceEqual(new[] { "b" }) && deleted.Restore.Count == 0 && deleted.Known.SetEquals(Set("a", "c")),
            "one replay deleted in game takes its copy");
        var dropped = ReplayKeeperPlan.Decide(Set("a"), Set("a", "b", "c"), Set(), Set("a", "b", "c"));
        Check(dropped.DropCopy.Count == 0 && dropped.KeptAtOnce == 2 && dropped.Restore.Count == 0 && dropped.Known.SetEquals(Set("a")),
            "several gone at once keep their copies and are not put back");
        var added = ReplayKeeperPlan.Decide(Set("a", "d"), Set("a"), Set(), Set("a"));
        Check(added.DropCopy.Count == 0 && added.KeptAtOnce == 0 && added.Known.SetEquals(Set("a", "d")), "a new save is known");
    }
}
