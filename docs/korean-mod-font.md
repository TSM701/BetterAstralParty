# Korean mod font

## Native typography — version 0.5.12

### 0.5.13: destination-specific inheritance (Korean-patched reference)

Cream labels and buff/relic tooltip clones inherit Common confirmation-label typography.
Dark character role strips, combat captions, fallback recommendation text and settings
button titles inherit Common Button_PreviewSkin typography (white fill, black outline).
Existing ability/card artwork retains its own native formatting. Damage digits copy the
live dice text formats and never enter general-purpose OS fallback. Field nicknames remain unchanged.
Neutral quick-advice digits inherit the body colour rather than forcing white. OS fallback
uses a pixel outline rather than normalized SDF parameters and restores native effects on return.
Help scrolling remeasures after a fallback change. No font files are added.
These corrections have automated checks; the reported dice appearance still needs in-game comparison.

No font binaries, character inventories or custom TMP atlases are shipped.
Cafe24, Jua and M PLUS resources are removed from source assets, distribution and the installed mod.
Historical backups are not distribution packages.

Native UI clones retain their text format. New labels copy Common UI typography.
Layout-specific size/alignment and semantic combat colours remain; no language-dependent
font choice or forced weight, outline, spacing or shadow. Game locale aliases and patch
mappings stay owned by the game. Field-buff player nickname code is unchanged and excluded.

Coverage uses native parsed text (not markup or image URLs), skipping whitespace/control
characters and inline image placeholders. TMP checks allow native dynamic glyph insertion,
but never append a fallback atlas: this FairyGUI TMP renderer binds its original atlas only.
A whole phrase switches to the native DynamicFont path for installed Malgun Gothic only
when native coverage is insufficient and Malgun Gothic covers the complete phrase.
When text changes, it can return to its native font. Unavailable fonts or unsupported phrases
keep native rendering. Errors must not disable UI. No OS/game font files are redistributed.

Tracked visible labels are checked at most ten times per second; unchanged text/font assets
skip glyph checks. Disposed labels are removed. Existing clone styles and native aliases
are preserved. DynamicFont and TMP render differently; visual equivalence is not promised.

Build, coverage-policy and native metadata checks do not replace in-game validation.
Patched/unpatched Korean/English menus, long descriptions and fallback appearance need testing.
