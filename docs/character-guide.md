# Character role and preparation guide

This feature reads the game's `CharacterTagType`/`CharacterInfoConfigure.TagType`.
It does not contain a character-ID classification list. All labels show only the role;
there are no slot recommendations or highlighted slots. Unknown future values stay unlabelled.

Role strips in both screens sit above the native `com_Qulity` skin grade. Text follows
the grade's transformed width; backing fills the portrait's full width, including side gaps.
The background extends through the grade's bottom and sits behind the grade and YOU.
Preparation keeps the portrait width and extends downward to the profile plate's
vertical midpoint, behind that plate, to cover the seam without widening the strip.
It stretches a solid lower-centre band of the native frame, retaining its texture
instead of mapping characters or rarity to colours. Preparation uses a private catalogue
frame template with native breakthrough/collaboration controllers to share this appearance.
In a PvE preparation screen, hovering the selected character illustration for 0.2 seconds
opens a private panel. No selection, ready or
slot-swap operation is called. The popup is inspectable without clicking; use the
wheel over it to read overflow content.

Skill names and descriptions are copied from the native prepared detail list,
including its skill-type controller. Cooldown comes from the prepared list renderer's
skill configuration. Rows reuse the catalogue's `Hero_Com_Skill`, including the native
header and cooldown icon. Potential uses `Hero_Com_FileBreakThrough` with its native
artwork and description, hiding unlock controls and unlock-state messages. Potential
descriptions come from the character's PveBreak IDs and native BreakDict/localization.
They describe available effects, not another player's verified unlock state.
Native game language is preserved for ability descriptions; mod tags use the mod language.

Discovery and content refresh run at most five times per second; only entrance
alpha follows frame timing. No fonts, global aliases, gameplay or network values change.
Failures disable only CharacterGuide, and scene changes release private popup objects.

Validation: `tools/test-character-guide.ps1` checks role-only labels, unknown
roles, Korean/English coverage and native binding metadata. Release build and existing
regressions pass. In-game hover, clipping, layering and appearance still require validation.

Version 0.5.0 introduces this feature on top of restored 0.4.92, without the discarded font changes.
Version 0.5.1 replaces floating capsules and slot guidance with native-aligned role strips
and catalogue-style ability/potential content.
Version 0.5.2 raises catalogue labels and joins their backing to the card footer;
preparation badges move to the upper-right corner behind OK.
Version 0.5.3 fills the side gaps and unifies both screens with lower role strips behind YOU.
Version 0.5.4 adds proportional side bleed beneath native trim and removes the potential
activation wordmark from hover details while preserving its title and effect description.
Version 0.5.5 removes preparation-screen side bleed and clips only the private strip's
bottom corners with a native rounded stencil mask. Catalogue strips remain unchanged.
Version 0.5.6 rounds all four preparation-strip corners. Descriptions containing raw
Astral tokens use the native MatchSkillTextContent resolver before layout, including
potential effects; already-resolved skill descriptions are preserved.
Version 0.5.7 checks only the four cached preparation slots each frame for character,
skin, selection and language changes. Changed slots invalidate text, backing and any
open tooltip together; unchanged slots retain their UI. Tree discovery remains throttled.
Version 0.5.8 keeps only the preparation strip's top corners rounded and extends its
backing to the union of portrait/profile widths and the profile's top edge, closing seams.
Version 0.5.9 removes that width expansion: preparation backing keeps portrait width,
overlaps underneath the profile plate, and explicitly stays behind its native layer.
Version 0.5.10 adds one UI unit of right-only preparation backing overlap for the thin
edge seam, without moving the left edge or text anchors.
Version 0.5.11 starts asynchronous Hero resource loading and prepares the private frame
template in the PvE preparation screen before selection. Load completion triggers an
immediate scan; selected states are committed only after badge rendering succeeds
(or an unknown role intentionally has no label), so deferred first renders retry each frame.
