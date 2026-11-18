namespace BetterAstralParty;

// Remove Replay Save Limit's copy decisions, free of the game so the self-test checks them (ReplayKeeperCheck); rules in
// .claude/rules/replays.md.
internal static class ReplayKeeperPlan
{
    internal sealed record Plan(IReadOnlyList<string> Restore, IReadOnlyList<string> DropCopy, int KeptAtOnce, ISet<string> Known);

    // `present`: the replays the game's folder holds now; `copies`: the ones kept outside it; `known`: what the game's folder
    // held at the last pass of an earlier run (the state file); `seen`: what it held at the previous pass of this run, null on
    // the run's first pass.
    // - First pass: a replay known but gone, with a copy, is put back. The game's 캐시 정리 deletes its whole Temp folder and
    //   quits at once, so no pass of that run sees the replays go.
    // - Later passes: one replay gone since the last pass is the player's delete (the saved tab deletes one at a time, behind
    //   a confirmation), and its copy goes too. Several gone at once (the saved tab drops every file it cannot read, or an
    //   outside cleaner) keep their copies and are not put back, so a game that keeps dropping them does not loop.
    internal static Plan Decide(ISet<string> present, ISet<string> copies, ISet<string> known, ISet<string>? seen)
    {
        var restore = new List<string>();
        var drop = new List<string>();
        var kept = 0;
        if (seen == null)
            restore.AddRange(known.Where(id => !present.Contains(id) && copies.Contains(id)).OrderBy(id => id, StringComparer.Ordinal));
        else
        {
            var gone = seen.Where(id => !present.Contains(id)).ToList();
            if (gone.Count == 1) { if (copies.Contains(gone[0])) drop.Add(gone[0]); }
            else kept = gone.Count(copies.Contains);
        }
        var next = new HashSet<string>(present, StringComparer.Ordinal);
        next.UnionWith(restore);
        return new Plan(restore, drop, kept, next);
    }
}
