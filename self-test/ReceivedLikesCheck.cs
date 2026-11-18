using BetterAstralParty;

// Received Likes (2026-11-15): the ledger keeps one like per sender and card in pick order, the praise message's target is
// read from "{MsgType}, {id}", and the 다-1 stamps settle in pick order.
static class ReceivedLikesCheck
{
    internal static void Run()
    {
        void Check(bool value, string name) { if (!value) throw new Exception("Received likes regression: " + name); }
        bool Near(float a, float b) => Math.Abs(a - b) < 0.001f;

        Check(ReceivedLikesMotion.TryTarget("11, 4006731", out var id) && id == 4006731, "target after the message type");
        Check(ReceivedLikesMotion.TryTarget("11,42", out id) && id == 42, "target without a space");
        foreach (var bad in new[] { null, "", "11", "11, ", "11, -5", "11, 0", "11, 1, 2", "11, x" })
            Check(!ReceivedLikesMotion.TryTarget(bad, out _), "rejects " + (bad ?? "null"));

        var likes = new LikeLedger();
        Check(likes.Empty && likes.For(3).Count == 0, "starts empty");
        Check(likes.Add(2, 3, 0.3f) && likes.Add(0, 3, 1.3f) && likes.Add(3, 2, 0.8f), "likes from other picks count");
        Check(!likes.Add(0, 3, 2f), "a sender likes a card once");
        Check(!likes.Add(1, 1, 2f) && !likes.Add(-1, 3, 2f) && !likes.Add(1, 4, 2f), "self-likes and unknown players drop out");
        Check(likes.Add(1, 3, 2.3f), "a later like from a lower pick");
        var card = likes.For(3);
        Check(card.Count == 3 && card[0] == (0, 1.3f) && card[1] == (1, 2.3f) && card[2] == (2, 0.3f), "senders in pick order with arrival times");
        Check(likes.For(2).Count == 1 && likes.For(0).Count == 0, "cards apart");
        likes.Clear();
        Check(likes.Empty, "cleared with the settlement");

        // FairyGUI turns clockwise for positive degrees, the draft's Pillow counter-clockwise: the signs are the draft's flipped.
        Check(ReceivedLikesMotion.Slant(0) == 8f && ReceivedLikesMotion.Slant(1) == -6f && ReceivedLikesMotion.Slant(2) == 4f
            && ReceivedLikesMotion.Slant(3) == -8f, "each pick keeps the draft's slant");
        Check(ReceivedLikesMotion.Lean(0f) == -12f && Near(ReceivedLikesMotion.Lean(0.25f), -6f) && ReceivedLikesMotion.Lean(0.5f) == 0f
            && ReceivedLikesMotion.Lean(-0.1f) == 0f, "a new stamp straightens over 0.5 s from the draft's side");
        Check(Near(ReceivedLikesMotion.Slide(0f, 10f, 1f), 10f) && ReceivedLikesMotion.Slide(0f, 10f, float.NegativeInfinity) == 0f
            && ReceivedLikesMotion.Slide(4f, 4f, float.NegativeInfinity) == 4f && Near(ReceivedLikesMotion.Slide(0f, 10f, 0.1f), 7.5f),
            "stamps slide to their new place over 0.2 s");
        Check(ReceivedLikesMotion.StampX(0, 1, 210f, 50f, 28f) == 188f && ReceivedLikesMotion.StampX(2, 3, 210f, 50f, 28f) == 188f
            && ReceivedLikesMotion.StampX(0, 3, 210f, 50f, 28f) == 88f && ReceivedLikesMotion.StampX(0, 4, 210f, 50f, 28f) == 38f,
            "stamps right-aligned, the last one at the same place");
        // Muted senders (2026-11-16): a muted human other than the local player gets a note, a friend-list block its own rows.
        Check(MutedSenders.Kind(true, false, false, false) == MutedSender.Match && MutedSenders.Kind(true, false, false, true) == MutedSender.Friend,
            "a muted sender, by battle window or friend list");
        Check(MutedSenders.Kind(false, false, false, true) == MutedSender.None && MutedSenders.Kind(true, true, false, false) == MutedSender.None
            && MutedSenders.Kind(true, false, true, false) == MutedSender.None, "not muted, the local player and bots get none");
        var match = MutedSenders.Rows(MutedSender.Match); var friend = MutedSenders.Rows(MutedSender.Friend);
        Check(match.Length == 3 && friend.Length == 3 && match[1] == friend[1] && match[0] != friend[0] && match[2] != friend[2]
            && match[2].Contains("이번 경기에만") && friend[0].Contains("친구 목록"), "two cases share the reason and differ in how it came and lasts");
        // Entrance 나: hidden behind the plate before it starts, resting after 0.35 s, overshooting above its place on the way.
        Check(ReceivedLikesMotion.Rise(-0.1f) == 1f && ReceivedLikesMotion.Rise(0f) == 1f && ReceivedLikesMotion.Rise(0.35f) == 0f
            && ReceivedLikesMotion.Rise(1f) == 0f && Near(ReceivedLikesMotion.Rise(0.3499f), 0f) && ReceivedLikesMotion.Rise(0.25f) < 0f
            && ReceivedLikesMotion.Rise(0.05f) is > 0f and < 1f, "the note rises from behind the plate with a small overshoot");
        Check(ReceivedLikesMotion.Pulse(0.5f) == 1f && ReceivedLikesMotion.Pulse(1.2f) == 1f && Near(ReceivedLikesMotion.Pulse(0.9f), 1.25f)
            && ReceivedLikesMotion.Pulse(0.8f) is > 1f and < 1.25f, "the badge swells once after the note rests");
        // Sender photos (2026-11-17) shrink by area with premultiplied colour: white beside transparent black stays white at
        // half cover instead of turning grey, the weights cover each span exactly, and the same size changes nothing.
        var stripes = new byte[4 * 4 * 4];
        for (var i = 0; i < 16; i += 2) stripes[i * 4] = stripes[i * 4 + 1] = stripes[i * 4 + 2] = stripes[i * 4 + 3] = 255;
        var half = PhotoScale.Downscale(stripes, 4, 2);
        Check(half.Length == 16 && Enumerable.Range(0, 4).All(p => half[p * 4] == 255 && half[p * 4 + 1] == 255 && half[p * 4 + 2] == 255 && half[p * 4 + 3] == 128),
            "a photo shrinks by area without darkening its outline");
        var white = Enumerable.Repeat((byte)255, 220 * 220 * 4).ToArray();
        Check(PhotoScale.Downscale(white, 220, 136) is { Length: 136 * 136 * 4 } shrunk && shrunk.All(b => b == 255), "220 to 136 keeps a solid photo solid");
        Check(PhotoScale.Downscale(stripes, 4, 4).SequenceEqual(stripes), "an unscaled photo stays as it is");
                Console.WriteLine("받은 좋아요 기록·움직임 검증 통과");
    }
}
