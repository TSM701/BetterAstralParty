# Korean patch resource analysis — 2026-09-25

## Scope and provenance

Read-only analysis of the user's WindowsPatcher resource-replacement route; no patcher execution and no game-file writes. Existing BundleInspector was reused.

- Patch source: https://github.com/maynut02/astral-party-korean-patch/tree/1d053089d9e7e230676dc0b1354368ff6018f5ae
- Installer source: https://github.com/maynut02/astral-party-auto-patcher/tree/42ed9b14b0b608833c43ad5f3f8533975460f4db/windows-patcher
- Payload: https://github.com/maynut02/astral-party-korean-patch/releases/tag/v3.2.0_r221_p0
- Original: https://github.com/maynut02/astral-party-korean-patch/releases/tag/v3.2.0_r221-original
- INT_STEAM manifest SHA-256: 5B1A3EC2466F663EA3CB6B4AF6F5CF84B273B536DA5AB894CCFACA0D24344586

The manifest's four paths match the local September 25 removal log. All eight downloaded gzip files and decompressed bundles matched the manifest hashes. Release tag says r221, but its internal game revision says 212; these identifiers are recorded as published, not assumed equivalent. Current public source is corroborating evidence, not proof that the supplied EXE was built from this exact commit.

## Observed binary differences

| Bundle | Comparison result |
| --- | --- |
| LANG | 2 entries, only the English TextAsset changed. Its name stayed English. XML retained 1,310 string keys; 1,106 values changed; 1,101 patched values contain Hangul. |
| STR | 158 entries, 61 changed; all changed entries are STR TextAssets, including STRBuff, STRCard, STRCharacter and STRRelic. |
| TMP font | 7 entries, 2 changed: Afacad-Regular_TMP MonoBehaviour and Afacad-Regular_TMP Atlas Texture2D. Names stayed unchanged. |
| data.unity3d | 10,941 entries, exactly 1 changed: resources.assets/242/128, Font Afacad-Regular. |

Example from the same XML key: Leave becomes 떠나기; Transfer becomes 송금.

## Source trace

routes/int_steam.yaml selects LANG target en / asset English, STR targetField en, TMP asset Afacad-Regular_TMP and legacy font Afacad-Regular.

builder/src/astral_builder/patch/builder.py extracts the target TextAssets. translations.py replaces approved translated values, preserves untranslated values, and updates only the selected STR language field. fonts.py replaces the TMP font data and atlas; patch_legacy_font replaces m_FontData inside the existing named Font asset.

windows-patcher/src/install.rs validates manifest and downloaded payload hashes, copies staged replacements to their target roots and verifies installed files. restore_release_files verifies staged release originals, restores replacement files and checks their original hashes. The local log confirms three Addressables __data files plus data.unity3d were restored. The newer windows-plugin implementation is separate and was not analyzed here.

## Conclusions for BetterAstralParty

This route does not add a separate Korean locale: it substitutes Korean content into the English locale and substitutes font contents while retaining original asset names. Locale enum and font-name checks alone cannot reliably detect the patch.

The Steam integrity incident is now conclusively explained: the patch manifest's data.unity3d hash 728E822D...512EE is exactly the old mod baseline, and source hash 948F75DC...73CC5 is exactly Steam's restored file. The earlier tentative explanation involving a local font rollback is superseded: the old file is the published Korean-patched version. Steam restored that file but did not restore the three external Addressables payloads.

The mod's hard-coded Korean UI relied on the patched font coverage. Removing the patch leaves English assets/fonts but Korean mod strings, explaining the missing text. An English UI fallback should localize mod-owned strings as well as use native fonts. Korean UI without the patch instead requires a separately validated Hangul-capable font. Automatic locale selection should inspect actual translated content and glyph support, with a user override; no implementation changes were made in this analysis.

Downloaded material is retained locally under .research/patch-bundle-review for reproduction. Game resources are not covered by the code's MIT license and should not be bundled into the mod.
