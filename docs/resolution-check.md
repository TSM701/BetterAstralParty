# 16:9 calculation audit — 2026-09-28

Tested production layout functions from source 0.28.9 with simulated FairyGUI
logical root dimensions. These are not screenshots or physical display/DPI tests.
No plugin source, configuration, installed files or version was changed.

| Root dimensions | New assertions | Result | Minimum focused outline-to-category gap |
|---|---:|---|---:|
| 1280 × 720 | 14,349 | PASS | 2.67 units |
| 1600 × 900 | 14,349 | PASS | 3.33 units |
| 1920 × 1080 | 14,349 | PASS | 4.00 units |
| 2560 × 1440 | 14,349 | PASS | 5.33 units |
| 3840 × 2160 | 14,349 | PASS | 8.00 units |

71,745 assertions in `ResolutionCheck` passed, along with the existing self-test
suite. `HandLayoutCheck` now also includes 900p and 4K (seven resolutions total).

Coverage:
- Hand: 1–7 columns, 1–11 cards per column, all cells, expanded card bounds,
  enlarged corner hit geometry, category clearance; existing tests additionally
  cover folded overlap, neighbour separation, centres and inter-card hover gaps.
- Settings: window entrance bounds and scroll-marker bounds with 0/7/20/50 rows.
- KO: configured scales 0.75/1/1.5, using the runtime height cap.
- Card/chip popups: 440/460-unit widths, 620/644/900/1400-unit heights,
  nine pointer positions and three scales; screen-edge clearance.
- Battle buff rows: both sides, 0–6 icons, pagination, three scales,
  twelve synthetic HP anchors; bounds and HP alignment.

Limits: actual font measurement and Korean/English wrapping are not simulated.
Translation existence is tested, not glyph widths. Native preparation/field UI,
recommendation anchors, transforms, real hit testing, dynamic resize propagation,
animations and interaction between simultaneously displayed panels are not fully
covered. Small positive geometry clearance does not guarantee readability.
FairyGUI logical dimensions may differ from physical display pixels. No claim
that every UI is fully validated at these physical resolutions.

Rerun from the repository root:
```powershell
& ./.dotnet/dotnet.exe run --project self-test/BetterAstralParty.SelfTest.csproj -c Release --no-restore
```
