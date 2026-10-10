using BetterAstralParty;

static class HandLayoutCheck
{
    internal static void Run()
    {
        void Check(bool value) { if (!value) throw new Exception("Grouped hand classification/layout regression"); }
        Check(HandLayout.Mode(false, "Click") == 0 && HandLayout.Mode(true, "Hover") == 1
            && HandLayout.Mode(true, "Click") == 2 && HandLayout.Mode(true, "invalid") == 1);
        Check(HandLayout.NextMode(false, "Hover") == 2 && HandLayout.NextMode(false, "Click") == 2
            && HandLayout.NextMode(true, "Click") == 1 && HandLayout.NextMode(true, "Hover") == 0);
        foreach (var h in new[] { 720f, 900f, 1080f, 1440f, 2160f })
        foreach (var aspect in new[] { 1.3f, 1.5f, 2f })
        for (var count = 1; count <= 24; count++)
        for (var columns = 1; columns <= 7; columns++)
        {
            var w = h * 16 / 9;
            var unit = h / 1080f;
            var pileTop = HandLayout.Place(w, h, columns, 0, count, 0, aspect).Y;
            HandLayout.Slot? previous = null;
            for (var i = 0; i < count; i++)
            {
                var slot = HandLayout.Focus(HandLayout.ClickRow(w, h, count, i, aspect, pileTop));
                Check(slot.Width > 0 && slot.Height > 0 && slot.X >= w * .25f - .01f
                    && slot.X + slot.Width <= w * .75f + .01f);
                Check(slot.Y - 12 * unit > h * .08f);
                Check(Math.Abs(pileTop - slot.Y - slot.Height - 16 * unit) < .01f);
                if (previous is { } p) Check(slot.X > p.X + p.Width && Math.Abs(slot.Y - p.Y) < .01f);
                previous = slot;
            }
        }
        Check(HandLayout.Classify(99999, 1) == HandGroup.Attack);
        Check(HandLayout.Classify(99999, 2) == HandGroup.Defense);
        Check(HandLayout.Classify(20019, 3) == HandGroup.Disrupt); // EffectType.Reply is not always healing.
        Check(HandLayout.Classify(21014, 3) == HandGroup.Support); // EffectType.Direct is not always damage.
        Check(HandLayout.Classify(21020, 3) == HandGroup.Move); // Teleport + heal: primary movement.
        Check(HandLayout.Classify(21016, 3) == HandGroup.Resource);
        Check(HandLayout.Classify(20003, 4) == HandGroup.Defense);
        foreach (var id in new[] { 0, 99999, 21017, 21022, 21023, 21025 })
            Check(HandLayout.Classify(id, 3) == HandGroup.Other);
        foreach (var group in Enum.GetValues<HandGroup>())
            Check(HandLayout.Label(group, true).Length > 0 && HandLayout.Label(group, false).Length > 0
                && HandLayout.Label(group, true) != HandLayout.Label(group, false));
        foreach (var (w, h) in new[] { (1280f,720f), (1600f,900f), (1920f,1080f), (2560f,1440f), (3840f,2160f), (3440f,1440f), (1024f,768f) })
        for (var columns = 1; columns <= 7; columns++)
        for (var rows = 1; rows <= 11; rows++)
        {
            var first = HandLayout.Place(w,h,columns,0,rows,0);
            var last = HandLayout.Place(w,h,columns,columns-1,rows,rows-1);
            Check(first.X >= w * .25f - 1 && last.X + last.Width <= w * .75f + 1);
            Check(Math.Abs(first.X + last.X + last.Width - w) < .01f);
            Check(first.Y > h * .5f && last.Y + last.Height < h);
            Check(first.Width > 0 && first.Step > 0 && first.Step < first.Height);
            if (rows > 1) Check(HandLayout.Place(w,h,columns,0,rows,1).Y < first.Y + first.Height);
            var expanded = HandLayout.Place(w,h,columns,0,rows,0, expanded: true);
            var expandedLast = HandLayout.Place(w,h,columns,0,rows,rows-1, expanded: true);
            Check(expanded.Step >= first.Step && expanded.Y >= h * .18f - 1);
            var foldedLast = HandLayout.Place(w,h,columns,0,rows,rows-1);
            var focusedLast = HandLayout.Focus(expandedLast);
            var unit = Math.Min(w / 1920f, h / 1080f);
            Check(Math.Abs(foldedLast.Y + foldedLast.Height - (h - 44 * unit)) < .01f);
            Check(focusedLast.Y + focusedLast.Height + 3 * unit < h - 37 * unit);
            Check(Math.Abs(focusedLast.Y + focusedLast.Height - foldedLast.Y - foldedLast.Height) < .01f);
            Check(expanded.Width > 0 && expanded.Height > 0 && expanded.Step > expanded.Height);
            Check(Math.Abs(expanded.X + expanded.Width / 2 - first.X - first.Width / 2) < .01f);
            Check(expandedLast.Y + expandedLast.Height < h);
            for (var row = 1; row < rows; row++)
            {
                var previous = HandLayout.Place(w,h,columns,0,rows,row-1, expanded: true);
                var current = HandLayout.Place(w,h,columns,0,rows,row, expanded: true);
                Check(current.Y > previous.Y + previous.Height);
                Check(current.Width == expanded.Width && current.Height == expanded.Height);
                Check(HandLayout.InColumn(current.X + current.Width / 2,
                    (previous.Y + previous.Height + current.Y) / 2, expanded, h, 12));
                var focusPrevious = HandLayout.Focus(previous);
                var focusCurrent = HandLayout.Focus(current);
                Check(focusPrevious.Y + focusPrevious.Height < focusCurrent.Y);
            }
            var focusFirst = HandLayout.Focus(expanded);
            Check(Math.Abs(focusFirst.X + focusFirst.Width / 2 - expanded.X - expanded.Width / 2) < .01f);
            Check(Math.Abs(focusFirst.Y + focusFirst.Height / 2 - expanded.Y - expanded.Height / 2) < .01f);
            // Every visible edge/corner of the enlarged artwork and its 3px outline is a focus hit.
            var border = 3 * Math.Min(w / 1920f, h / 1080f);
            foreach (var x in new[] { focusFirst.X - border, focusFirst.X, focusFirst.X + focusFirst.Width / 2, focusFirst.X + focusFirst.Width + border })
            foreach (var y in new[] { focusFirst.Y - border, focusFirst.Y, focusFirst.Y + focusFirst.Height / 2, focusFirst.Y + focusFirst.Height + border })
                Check(HandLayout.InFocus(x,y,expanded,border));
            Check(!HandLayout.InFocus(focusFirst.X-border-1, focusFirst.Y, expanded,border));
            Check(focusFirst.Width > expanded.Width && focusFirst.Height > expanded.Height);
            Check(focusFirst.X >= first.X - .01f && focusFirst.X + focusFirst.Width <= first.X + first.Width + .01f);
            Check(focusFirst.Y >= h * .18f - .01f);
            Check(HandLayout.InColumn(expanded.X + 5, expanded.Y + 5, expanded, h, 12));
            Check(!HandLayout.InColumn(expanded.X - 13, expanded.Y, expanded, h, 12));
            if (columns > 1) Check(first.X + first.Width < HandLayout.Place(w,h,columns,1,rows,0).X);
        }
        Check(HandLayout.Resting(100, 200, 100, 200));
        Check(!HandLayout.Resting(0, 1080, 100, 800)); // Waiting to be dealt, not an elapsed-time guess.
        Check(!HandLayout.Resting(100, 500, 100, 800));
        Check(HandLayout.CanResume(true, false, false, false)); // Idle off-target cards must recover.
        Check(!HandLayout.CanResume(false, false, false, false));
        Check(!HandLayout.CanResume(true, true, false, false));
        Check(!HandLayout.CanResume(true, false, true, false));
        Check(!HandLayout.CanResume(true, false, false, true));
        foreach (var t in new[] { 0f, .1f, .5f, .9f, .99f })
        {
            const float current = 480, end = 900;
            var start = HandLayout.RetargetStart(current, end, t);
            Check(Math.Abs(start + (end-start)*t-current) < .01f);
            Check(Math.Abs(start + (end-start)-end) < .01f);
        }
        ModText.Select("English");
        Check(ModText.Text(MenuLayout.Help("HandLayout").Title) == "Grouped Hand");
        Check(!ModText.Text(MenuLayout.Help("HandLayout").Body).Contains("필드"));
        ModText.Select("한국어");
        Console.WriteLine("Grouped hand: classification, KO/EN, non-overlapping equal-size unfolded cards, fixed bottom/column centre and gap hover; 1-7 groups and 1-11 rows across seven resolutions (including HD through 4K) passed.");
    }
}
