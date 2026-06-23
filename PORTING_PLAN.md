# PKHeX Cross-Platform Porting Plan (macOS + Linux)

## TL;DR

The engine is already portable. The job is **building a new cross-platform UI on top of the
existing `PKHeX.Core`**, plus swapping the imaging layer off the Windows-only `System.Drawing`.
No untangling of business logic from Windows is required — the codebase is already cleanly layered.

| Layer | Lines | Current target | Port effort |
|---|---|---|---|
| `PKHeX.Core` | ~228k | `net10.0` (portable) | **None** — runs as-is |
| `PKHeX.Drawing.*` (3 projects) | ~75k | `net10.0-windows` (System.Drawing) | **Moderate** — swap imaging lib |
| `PKHeX.WinForms` | ~125k (112 forms, 42 controls) | WinForms | **Large** — full UI rewrite |

---

## Why this is tractable

Confirmed by inspecting the repo:

- `PKHeX.Core` targets plain `net10.0`, has **zero** references to `System.Windows.Forms` or
  `System.Drawing`, and **zero** `DllImport`/P-Invoke. All save parsing, Pokémon data,
  encryption, and legality logic lives here and runs on macOS/Linux unchanged.
- The UI consumes Core cleanly (184 WinForms files `using PKHeX.Core`), so the boundary is
  already a real API surface, not spaghetti.
- The only native calls in the whole app are 4 trivial `user32.dll` uses (cursor/window
  tweaks) — discard them on non-Windows.

The cost is concentrated in two places: the imaging layer and the ~150 UI screens.

---

## Recommended stack

- **UI framework: Avalonia.** Mature, true cross-platform (Windows/macOS/Linux from one
  codebase), and the closest mental model to WinForms (XAML + code-behind, similar control
  set). Best fit for a fork that needs to track a fast-moving upstream.
  - *Alternatives:* .NET MAUI (weaker Linux story); Blazor/Photino web UI (viable since Core
    is just a library, but a bigger paradigm shift).
- **Imaging: SkiaSharp** (cross-platform, high-performance, what Avalonia uses under the hood
  — so one rendering stack). *Alternative:* ImageSharp (pure managed, simpler API, slower for
  heavy sprite compositing).

---

## Phased plan

### Phase 0 — Prove the engine runs (1–2 days)
- Fork the repo; set up CI on macOS + Linux runners.
- Build and run the **Tests** project against `PKHeX.Core` on both OSes. This validates the
  thesis cheaply before committing to UI work.
- **Exit criterion:** Core test suite green on macOS and Linux.

### Phase 1 — Port the drawing layer off System.Drawing (2–4 weeks)
The three `PKHeX.Drawing.*` projects are `net10.0-windows` *only* because of
`System.Drawing.Common` (Windows-only since .NET 7; throws `PlatformNotSupportedException`
elsewhere). The surface is dominated by `Bitmap` (sprite assembly, color/shiny tinting, image
composition) plus a little `System.Drawing.Imaging` and QR generation.
- Introduce a thin internal imaging abstraction (e.g. `IImage`/`ICanvas`) or adopt SkiaSharp
  types directly across the three projects.
- Reimplement sprite compositing, tinting, and QR rendering on SkiaSharp.
- Retarget the three projects to plain `net10.0`.
- Note: much of the `Bitmap` surface is in generated `Resources.Designer.cs` files (each
  sprite is a property returning `Bitmap`) — these regenerate against the new image type, so
  the change is mechanical but wide.
- **Exit criterion:** `PKHeX.Drawing.*` build and produce correct sprites on all three OSes
  (diff a few rendered sprites against Windows output).

### Phase 2 — App shell + main window (3–6 weeks)
- New Avalonia app project (`PKHeX.Avalonia`) referencing Core + the ported Drawing projects.
- Rebuild the shell: `MainWindow`, menu/toolbar, drag-and-drop save loading, the **SAV
  Editor** and **PKM Editor** composite controls, and the **box/slot viewer** (`Controls/Slots`).
- Establish reusable patterns here (MVVM, data binding to Core objects, a sprite-loading
  service) — every later screen reuses them.
- **Exit criterion:** open a save, view boxes, edit and save a Pokémon round-trip on macOS.

### Phase 3 — Save-editor screens, generation by generation (the long pole, 2–6 months)
The bulk of the work: `Subforms/Save Editors` spans Gen1–Gen9 plus Misc, ~112 forms total.
Recommended order (ship value incrementally rather than big-bang):
1. **Cross-gen / Misc editors** first (trainer info, items/inventory, Pokédex, mystery gift)
   — highest reuse, used by the most people.
2. **Recent generations (Gen8/Gen9)** — most active user base.
3. **Older generations (Gen1–Gen7)** in descending popularity.
- Each screen is: re-create the layout in XAML, bind to the existing Core save object, port the
  code-behind event logic (which mostly already calls Core).
- **Exit criterion (per gen):** feature parity with the WinForms editor for that gen.

### Phase 4 — Polish & packaging (2–4 weeks)
- Plugin system (`Util/Plugins`) — decide whether to support, and adapt its loading model.
- Settings, localization (`.resx` strings carry over), file dialogs, high-DPI.
- Packaging: `.app` bundle (macOS), AppImage/Flatpak or tarball (Linux). Note macOS code
  signing/notarization if distributing publicly.

---

## Effort summary

- Drawing layer: **~2–4 weeks**
- Minimal usable editor (shell + cross-gen + 1–2 recent gens): **~2–3 months**
- Full feature parity across all generations: **~6+ months** for one experienced .NET dev.

Parallelizes well after Phase 2 — once the patterns are set, screens can be split across
contributors.

---

## Risks & caveats

- **Upstream is deliberately Windows-only.** A port lives as a fork; you carry the burden of
  merging frequent upstream updates into a divergent UI. Keeping Core untouched (consume it as
  a submodule/package) minimizes merge pain — do **not** fork Core's internals.
- **Designer files don't translate.** The 154 `.Designer.cs` layouts are WinForms-specific;
  every screen is hand-rebuilt in XAML. This is the real cost, not a technical blocker.
- **Sprite fidelity.** Validate SkiaSharp output pixel-against-pixel with Windows builds early
  (Phase 1) to avoid subtle tinting/scaling regressions surfacing late.
- **Pace of change.** PKHeX ships updates very frequently (new games, event data). Budget
  ongoing maintenance, not just the one-time port.

---

## First concrete step

Fork → run the `Tests` project against `PKHeX.Core` on a Mac. If it's green (it should be),
the entire premise is validated and you can commit to the UI build with confidence.
