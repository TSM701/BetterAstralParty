using System.Security.Cryptography;
using System.Text;
using BetterAstralParty.Updating;

// Sequence-combination check: every short sequence of whole installs, in-game updates and approved channel
// transitions runs through the production receipt/trust code and must match one written-down policy.
internal static class InstallSequences
{
    private const string Root = "ABCD1234:0000000000000100";
    private const string KeyId = "ephemeral-transition-fixture";
    private static readonly string[] Versions = { "1.4.1", "1.4.2", "1.4.2*", "1.4.3" }; // * = same version, different signed package
    private sealed record Package(string Channel, string Version, byte[] Bytes, byte[] Signature)
    {
        internal string Tag => Version.TrimEnd('*');
        internal string Digest => UpdateTrust.Hash(Bytes);
    }
    private sealed record Row(string Version, string Digest);
    // The intended policy, independent of the production code paths.
    private sealed class Model
    {
        internal string? Active; internal Row? Installed, High;
        internal readonly Dictionary<string, Row> Rows = new(StringComparer.Ordinal);
        internal Model Copy() { var m = new Model { Active = Active, Installed = Installed, High = High }; foreach (var (k, v) in Rows) m.Rows[k] = v; return m; }
        internal string Key() => $"{Active}:{Installed?.Version}:{string.Join(",", Rows.OrderBy(r => r.Key).Select(r => r.Key + "=" + r.Value.Version + (r.Value.Digest[..4])))}:{High?.Version}";
    }
    private static int Compare(string a, string b) => UpdateVersion.Parse(a).CompareTo(UpdateVersion.Parse(b));
    private static bool AboveFloor(Model m, string channel, Package p) =>
        !m.Rows.TryGetValue(channel, out var row) || Compare(p.Tag, row.Version) > 0 || Compare(p.Tag, row.Version) == 0 && p.Digest == row.Digest;
    private static void Apply(Model m, Package p)
    {
        m.Active = p.Channel; m.Installed = new(p.Tag, p.Digest); m.Rows[p.Channel] = m.Installed;
        if (m.High == null || Compare(p.Tag, m.High.Version) > 0) m.High = m.Installed;
    }
    // Whole install: repair the exact installed package; otherwise never below the package's channel floor.
    // Switching channel also needs a version newer than installed and not below the highest ever applied
    // (equal only with that exact package); the highest-version rule does not apply within one channel.
    private static bool ExpectWhole(Model m, Package p)
    {
        if (m.Installed == null) return true;
        if (p.Tag == m.Installed.Version && p.Digest == m.Installed.Digest) return AboveFloor(m, p.Channel, p);
        if (p.Channel != m.Active && (Compare(p.Tag, m.Installed.Version) <= 0
            || m.High != null && (Compare(p.Tag, m.High.Version) < 0 || Compare(p.Tag, m.High.Version) == 0 && p.Digest != m.High.Digest))) return false;
        return AboveFloor(m, p.Channel, p);
    }
    // In-game update: same channel, strictly newer than installed.
    private static bool ExpectUpdate(Model m, Package p) => m.Installed != null && p.Channel == m.Active && Compare(p.Tag, m.Installed.Version) > 0;
    // Approved transition: another channel, never below its floor (the same signed package may be replayed).
    private static bool ExpectTransition(Model m, Package p) => m.Installed != null && p.Channel != m.Active && AboveFloor(m, p.Channel, p);

    // Production state threaded through the real receipt and trust code.
    private sealed class Real
    {
        internal InstallReceipt? Receipt; internal string? Installed; internal string? High, HighDigest;
        internal Real Copy() => new() { Receipt = Receipt, Installed = Installed, High = High, HighDigest = HighDigest };
    }
    // The production high-water rule decides; this only reads back the record it would write.
    private static void Water(Real r, VerifiedUpdateDescriptor d)
    {
        var next = UpdateTransaction.NextWater(Root, r.High, d); if (next == null) return;
        var lines = Encoding.ASCII.GetString(next).Split('\n');
        r.High = lines[2]["version=".Length..]; r.HighDigest = lines[3]["descriptor=".Length..];
    }
    private static bool RealWhole(Real r, Package p, UpdateTrust trust)
    {
        var feed = ReleaseFeedPolicy.For(p.Channel);
        var d = trust.VerifyProposal(p.Bytes, KeyId, p.Signature, new UpdateContext(feed.Repository, feed.Channel, "0.0.0", p.Tag, 123, 456, UpdateTrust.PlatformId, repositoryId: feed.RepositoryId));
        try
        {
            if (r.Receipt == null) r.Receipt = InstallReceipt.Parse(InstallReceipt.Create(Root, d, null));
            else
            {
                InstallProof.ValidateChannel(r.Receipt.Channels, Root, r.Installed!, UpdateTrust.Protocol, 1, d, r.High, r.HighDigest);
                r.Receipt = InstallReceipt.Parse(InstallReceipt.Parse(r.Receipt.Updated(d)).InitializeChannel(Root, d));
            }
        }
        catch (Exception e) when (e is UpdateValidationException or ApplySafetyException) { return false; }
        r.Installed = p.Tag; Water(r, d); return true;
    }
    private static bool RealInGame(Real r, Package p, UpdateTrust trust, bool transition)
    {
        if (r.Receipt?.Channels == null) return false;
        var state = r.Receipt.Channels; var feed = ReleaseFeedPolicy.For(p.Channel);
        try
        {
            ChannelTransitionIntent? intent = null;
            if (transition)
            {
                var proposal = trust.VerifyProposal(p.Bytes, KeyId, p.Signature, new UpdateContext(feed.Repository, feed.Channel, "0.0.0", p.Tag, 123, 456, UpdateTrust.PlatformId, repositoryId: feed.RepositoryId));
                var review = new ChannelTransitionReview(Root, new string('E', 64), new string('F', 64), new string('A', 64), r.Installed!, state, proposal);
                intent = ChannelTransitionIntent.Parse(ChannelTransitionIntent.Approve(review).Bytes());
            }
            var selection = new UpdateContext(feed.Repository, feed.Channel, r.Installed!, p.Tag, 123, 456, UpdateTrust.PlatformId, null, feed.RepositoryId, intent);
            if (UpdateTransaction.WrongTarget(state, selection)) return false; // ApplyCore's own channel gate.
            var floorDigest = state.DescriptorFor(p.Channel);
            var context = new UpdateContext(selection.Repository, selection.Channel, r.Installed!, selection.ReleaseTag, selection.ReleaseId, selection.AssetId, selection.Platform,
                state.Floor(selection), selection.RepositoryId, selection.Transition, floorDigest == "-" ? null : floorDigest);
            var d = trust.VerifyDescriptor(p.Bytes, KeyId, p.Signature, context);
            var next = state.Applied(Root, d, intent?.Operation ?? Guid.NewGuid().ToString("N"), intent);
            r.Receipt = InstallReceipt.Parse(r.Receipt.Updated(d, next)); r.Installed = p.Tag; Water(r, d);
            return true;
        }
        catch (Exception e) when (e is UpdateValidationException or ApplySafetyException or ArgumentException) { return false; }
    }

    internal static (int Sequences, List<string> Mismatches) Run(RSA key, UpdateTrust trust)
    {
        var packages = new List<Package>();
        foreach (var feed in new[] { ReleaseFeedPolicy.Stable, ReleaseFeedPolicy.PreRelease, ReleaseFeedPolicy.Beta })
            foreach (var version in Versions)
            {
                var tag = version.TrimEnd('*');
                var text = $"BetterAstralParty.UpdateDescriptor/v5\nrepository={feed.Repository}\nversion={tag}\nchannel={feed.Channel}\nplatform={UpdateTrust.PlatformId}\nrelease-id=123\nasset-id=456\nasset-name=BetterAstralParty-{tag}-update.zip\nzip-bytes=1\nzip-sha256={new string(version.EndsWith('*') ? 'C' : 'A', 64)}\nfile-count={InstallReceipt.Owned.Length}\npurpose=release/{feed.Channel}\nrepository-id={feed.RepositoryId}\nsettings-schema=1\n";
                foreach (var path in InstallReceipt.Owned.OrderBy(v => v, StringComparer.Ordinal)) text += $"file={path}\t1\t{new string('B', 64)}\n";
                var bytes = Encoding.ASCII.GetBytes(text);
                packages.Add(new(feed.Channel, version, bytes, key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)));
            }
        var operations = new List<(string Name, Package P, int Kind)>(); // 0 whole, 1 update, 2 transition
        foreach (var p in packages) { operations.Add(("W " + p.Channel + " " + p.Version, p, 0)); operations.Add(("U " + p.Channel + " " + p.Version, p, 1)); operations.Add(("T " + p.Channel + " " + p.Version, p, 2)); }
        var mismatches = new List<string>(); var count = 0;
        // Breadth-first over three operations after a first whole install, deduplicated by model state.
        var frontier = new List<(Model M, Real R, string Path)>();
        foreach (var p in packages)
        {
            var m = new Model(); var r = new Real();
            if (!RealWhole(r, p, trust)) { mismatches.Add("first install refused: " + p.Channel + " " + p.Version); continue; }
            Apply(m, p); frontier.Add((m, r, "W " + p.Channel + " " + p.Version));
        }
        for (var depth = 0; depth < 3; depth++)
        {
            var next = new List<(Model, Real, string)>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (m, r, path) in frontier)
                foreach (var (name, p, kind) in operations)
                {
                    count++;
                    var expected = kind == 0 ? ExpectWhole(m, p) : kind == 1 ? ExpectUpdate(m, p) : ExpectTransition(m, p);
                    var rr = r.Copy();
                    var actual = kind == 0 ? RealWhole(rr, p, trust) : RealInGame(rr, p, trust, kind == 2);
                    if (actual != expected) { mismatches.Add($"{path} -> {name}: expected {(expected ? "allow" : "refuse")}, production {(actual ? "allowed" : "refused")}"); continue; }
                    if (!actual) continue;
                    var mm = m.Copy(); Apply(mm, p);
                    if (rr.Receipt!.Channels!.ActiveChannel != mm.Active || rr.Installed != mm.Installed!.Version || rr.High != mm.High!.Version || rr.HighDigest != mm.High.Digest)
                    { mismatches.Add($"{path} -> {name}: state differs (production {rr.Receipt.Channels.ActiveChannel} {rr.Installed} high {rr.High})"); continue; }
                    if (seen.Add(mm.Key())) next.Add((mm, rr, path + " -> " + name));
                }
            frontier = next;
        }
        return (count, mismatches);
    }
}
