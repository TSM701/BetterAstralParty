# Performance review — 0.4.87

## Evidence

The 0.4.84 diagnostic log records home menu creation from 03:57:23.422 to
03:57:25.476 (about 2.05 seconds). That sample reports gameui_max_ms=2066.345.
This establishes a mod-owned first-use stall, but does not isolate all of its
cost. ModFont previously baked the entire 2,350+ character inventory inside
that synchronous path. The font and portrait/native asset loading were not
separately timed. A second menu creation immediately followed language refresh.

During later gameplay a sample at 04:06:15 reports ui_avg_ms=0.141,
ui_max_ms=11.745 and managed_bytes_per_frame=5784. These measurements exclude
native rendering/layout and do not establish total mod overhead or GC cost.

Windows Application Error events at 03:26:19 and 03:33:02 identify FlowSDK.dll,
exception 0xc0000005, offset 0x22fd68. This is not proof either that the mod caused
the crash or that it is unrelated. No SDK bypass or speculative gameplay/native
memory patch is included, and this release must not be described as a crash fix.

## Changes

- Seed only the mod menu title in the bundled font; use the existing native TMP
  dynamic glyph path for other text on demand. Keep the same font, atlas size,
  single-page restriction, fallback and license. The full inventory remains a
  build-time coverage check. Log font initialization duration separately.
- Preserve a home menu already created for the current language revision,
  avoiding immediate duplicate disposal, portrait loading and UI construction.
- Skip unchanged damage-indicator scale/position/alpha writes during its hold.
  Keep per-frame entrance/exit animation, responsive resizing and UI scaling.

## Reviewed but unchanged

Catalog verification already hashes in a worker after initial validation. Field,
battle and popup discovery already use throttled scans; character-name traversal
is sliced with a per-frame budget. Diagnostic flushing is retained to preserve
the last lines before a crash. Native interop validation and feature isolation
remain intact. All features, public-data restrictions and vanilla launch remain.

## Validation and remaining work

Release and regression checks cover single-atlas/font coverage, native dynamic
font lookup, menu language guard, damage write guards and existing calculation,
UI timing and interop checks. Wiring checks are not performance benchmarks.
No new game process was launched and no before/after FPS improvement is claimed.
Verify first home entry, Korean settings/help, solo battle text and font init
timing in a fresh run; compare against the 2.05-second baseline. Investigating
the native crash further requires its dump/call stack or a controlled vanilla
comparison, not just the faulting module name.
