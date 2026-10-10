using BetterAstralParty;

// Production pure layout functions; dimensions are simulated FairyGUI logical root units.
// No claim about physical DPI, font measurement, native raycasts or runtime resize events.
static class ResolutionCheck
{
    internal static void Run()
    {
        foreach (var (w,h) in new[] { (1280f,720f),(1600f,900f),(1920f,1080f),(2560f,1440f),(3840f,2160f) })
        {
            var checks=0;
            void Check(bool ok, string name) { checks++; if(!ok) throw new Exception($"Resolution {w}x{h}: {name}"); }
            void Inside(float x,float y,float width,float height,float margin,string name) =>
                Check(float.IsFinite(x+y+width+height) && width>0 && height>0 && x>=margin-.02f && y>=margin-.02f
                    && x+width<=w-margin+.02f && y+height<=h-margin+.02f,name);
            var menuScale=MenuLayout.FitScale(w,h);
            foreach(var entrance in new[] { 0f,.5f,1f })
                Inside((w-MenuLayout.Width*menuScale)/2,(h-MenuLayout.Height*menuScale)/2+(1-entrance)*24,
                    MenuLayout.Width*menuScale,MenuLayout.Height*menuScale,0,"settings entrance bounds");
            foreach(var rows in new[] { 0,7,20,50 })
            foreach(var fraction in new[] { 0f,.5f,1f })
            {
                var content=MenuLayout.ContentHeight(rows==0 ? 0 : MenuLayout.ToggleY(rows-1)+MenuLayout.ToggleHeight-MenuLayout.SettingsY);
                var marker=MenuLayout.ScrollMarker(MenuLayout.SettingsHeight,content,(content-MenuLayout.SettingsHeight)*fraction);
                Check(marker.Y>=0 && marker.Height>=24 && marker.Y+marker.Height<=MenuLayout.SettingsHeight+.01f,"scroll bounds");
            }
            foreach(var desired in new[] { .75f,1f,1.5f })
            {
                // CardDiceUi caps the configured scale by logical root height.
                var scale=Math.Max(.1f,Math.Min(desired,h/1080f));
                var dice=CardDiceLayout.Place(w,h,CardDiceLayout.Width*scale,CardDiceLayout.Height*scale);
                Check(dice!=null,"KO visible");
                Inside(dice!.Value.X,dice.Value.Y,CardDiceLayout.Width*scale,CardDiceLayout.Height*scale,16,"KO bounds");
                foreach(var cw in new[] { 440f,460f })
                foreach(var ch in new[] { 620f,644f,900f,1400f })
                foreach(var x in new[] { 0f,w/2,w })
                foreach(var y in new[] { 0f,h/2,h })
                {
                    var popup=CardPreviewLayout.Place(w,h,x,y,desired,cw,ch);
                    Inside(popup.X,popup.Y,cw*popup.Scale,ch*popup.Scale,16,"card/chip popup bounds");
                }
                foreach(var attacker in new[] { false,true })
                foreach(var count in Enumerable.Range(0,7))
                foreach(var paged in new[] { false,true })
                foreach(var x in new[] { 16f,w*.3f,w*.7f,w-16 })
                foreach(var y in new[] { 0f,h*.7f,h-20 })
                {
                    var width=BattleStatusLayout.RowWidth(count,paged);
                    var buff=BattleStatusLayout.BelowHp(x,y,width,desired,w,h,attacker);
                    Check(buff!=null,"buff positive available space");
                    var b=buff!.Value;
                    Inside(b.X,b.Y,width*b.Scale,BattleStatusLayout.Height*b.Scale,8,"buff row bounds");
                    Check(Math.Abs((attacker ? b.X+width*b.Scale : b.X)-x)<.02f,"HP anchor alignment");
                }
            }
            // Every cell, not just the first/last column. Test the enlarged footprint and its hit corners.
            var unit=w/1920f;
            var minTextGap=float.MaxValue;
            for(int columns=1;columns<=7;columns++)
            for(int column=0;column<columns;column++)
            for(int rows=1;rows<=11;rows++)
            for(int row=0;row<rows;row++)
            {
                var cell=HandLayout.Place(w,h,columns,column,rows,row,expanded:true);
                var focus=HandLayout.Focus(cell);
                Inside(focus.X,focus.Y,focus.Width,focus.Height,0,"expanded card bounds");
                var gap=h-37*unit-(focus.Y+focus.Height+3*unit);
                minTextGap=Math.Min(minTextGap,gap);
                Check(gap>0,"focus outline vs category label");
                foreach(var x in new[] { focus.X-3*unit,focus.X+focus.Width+3*unit })
                foreach(var y in new[] { focus.Y-3*unit,focus.Y+focus.Height+3*unit })
                    Check(HandLayout.InFocus(x,y,cell,3*unit),"focus corner hit");
            }
            Console.WriteLine($"RESOLUTION PASS {w}x{h}: {checks} assertions; minimum hand-outline/category gap={minTextGap:F2} logical units.");
        }
        Console.WriteLine("Resolution coverage: geometry only. Native anchors/fonts/text clipping/raycast/animation/physical DPI require runtime; no whole-UI certification.");
    }
}
