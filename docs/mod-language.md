# Mod languages (0.4.73)

Open Mod Settings (F8 on the main menu), then General, then Language.
Each click cycles Auto -> Korean -> English. Changes save immediately and
recreate only mod-owned UI so cached labels and fonts update together.
Existing saved Korean/English choices are preserved; new installations default
to Auto. An unknown configuration value is treated as Auto.

Auto reads the existing native Activity menu title every home refresh:
Hangul means Korean; other nonempty titles mean English. It does not infer the
language from a font, executable hash, player nickname or OS locale. Before a
valid menu sample is available it defaults to English, then keeps the last
known choice if the title is temporarily empty. A future patch that does not
translate that title can use the manual override. No native UI text is modified.

Since 0.5.12 both languages reuse native typography without bundled fonts.
Unsupported phrases may use installed Malgun Gothic as a whole-label fallback;
field-buff player nicknames are excluded. This does not translate
game-provided card/buff descriptions, character names, chat or account names:
those remain in the game's language. Debug logs and stable configuration keys
are not a user-facing localization surface and keep their existing wording.

Mod settings/help, short advice, effect counters, ping hints and compatibility
messages have English translations. English additive ATK/DEF descriptions are
accepted only by a full-string parser and remain cross-checked against public
card configuration. Unknown and compound effects fail closed.

Settings use Features and General tabs. Button dimensions remain fixed; native
text shrinking handles longer titles without enlarging or moving controls.
Automated tests cover auto/manual selection, translation coverage, formatting,
tab membership and safe card parsing. In-game appearance remains a manual check.
