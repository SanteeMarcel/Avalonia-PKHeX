# CLAUDE.md — PKHeX cross-platform port (macOS/Linux)

Context for continuing this project. This is a **fork of PKHeX** (a Pokémon save editor that
upstream ships Windows-only) being ported to run natively on macOS and Linux. The strategy:
**keep `PKHeX.Core` untouched and portable, replace the Windows-specific layers.** Read this
before implementing new features so you don't rediscover the gotchas the hard way.

---

## Goal & strategy

Upstream PKHeX is three tiers:
- **`PKHeX.Core`** (~228k LOC) — all save parsing, Pokémon data, legality, encryption. Targets
  plain `net10.0`, **zero** Windows dependencies. Runs on macOS/Linux **unmodified**. Do NOT fork
  its internals — consume it as-is so upstream updates stay mergeable.
- **`PKHeX.Drawing.*`** (3 projects) — sprite/image code. Was `net10.0-windows` via
  `System.Drawing.Common`. **Ported to SkiaSharp** (done).
- **`PKHeX.WinForms`** (~125k LOC, 112 forms) — the UI. Windows Forms, does not run off Windows.
  **Being replaced by a new Avalonia app**, screen by screen. This is the bulk of remaining work.

We are NOT porting WinForms; we are building a new Avalonia front-end on top of the (portable)
Core + (ported) drawing layer.

---

## Current status (what works)

- **Phase 0** ✅ Core builds and its test suite passes on macOS.
- **Phase 1** ✅ Entire drawing layer ported off `System.Drawing` → SkiaSharp:
  `PKHeX.Drawing`, `PKHeX.Drawing.PokeSprite`, `PKHeX.Drawing.Misc` all build clean.
- **Phase 2** ✅ `PKHeX.Avalonia` shell: loads a save, renders boxes + party + trainer info +
  box wallpaper, navigates boxes. Party members render as a clickable row above the box grid
  (reads `sav.PartyData`); they share the box slots' selection/legality/transfer behavior.
- **Editing** ✅ **Double-click any box or party slot** → `EditPokemonWindow` opens on a CLONE of
  that Pokémon (Cancel discards). Edit nickname (+ nicknamed flag), level, nature, ability (slot
  dropdown from the species' `PersonalInfo` abilities), held item (searchable `AutoCompleteBox`),
  4 moves (searchable `AutoCompleteBox`), IVs (0–31) and EVs (0–252) in HP/Atk/Def/Spe/SpA/SpD
  order, gender / form (name dropdown via `FormConverter.GetFormList`) / ball (`GameStrings.balllist`) /
  friendship, OT name + OT gender + TID/SID (`DisplayTID`/`DisplaySID`) + met level + met location
  (name dropdown via `GameInfo.GetLocationList(pk.Version, pk.Context)`, with a synthetic fallback
  entry for an out-of-list location id), a met-date picker (`pk.MetDate` `DateOnly?` ↔ `DatePicker`,
  with a Clear button; null on formats that don't store one), **markings** (named shapes via the
  generation's `IAppliedMarkings3/4/7` interface — bool checkboxes for Gen3–6, None/Blue/Pink combos
  for Gen7+) and **ribbons** (enumerated by `RibbonInfo.GetRibbonInfo` + `ReflectUtil` get/set, in a
  collapsed Expander; bool ribbons as checkboxes, count ribbons as numerics up to `MaxCount`), a
  Make-Shiny shortcut, and a **PID hex field + 🎲 Reroll** (`SetPIDGender`; Gen3–5 nature/gender/
  ability-slot changes are embedded into the PID via `SetPIDNature`/`SetPIDGender`/`SetAbilityIndex`
  in `WriteControlsToPk`, guarded so the PID only rerolls when the value actually changed). Markings/ribbons are built into two `FieldForm`s and load/save alongside the
  rest of the controls. **Moves are PP-aware**: each move has a per-move PP-Ups control (0–3); on
  save the PP-Ups are written before `FixMoves()` (which reorders empties to the end and zeroes
  their PP/PP-Ups, carrying PP-Ups along with the reorder), then `HealPP()` sets current PP to each
  move's max for its PP-Ups — avoids PP legality flags. A **legality panel** shows the verdict + `LegalityAnalysis.Report()`; the **Re-check**
  button writes the controls into the clone and refreshes the sprite + report without closing
  (iterate until legal). Apply → `RefreshChecksum` → caller writes the clone back to the originating
  slot (`SetBoxSlotAtIndex` for box, `ResetPartyStats` + `SetPartySlotAtIndex` for party) and
  re-renders. The editor coexists with the two-save transfer view (it doesn't replace it).
  Item/move/ball name↔id maps come from `GameInfo.GetStrings("en")`; an unrecognised typed move/item
  name leaves that field unchanged. Built code-behind with named XAML controls (reuses the `Opt`
  combo record from `Gen5Forms.cs`). All edit writes funnel through one `WriteControlsToPk()` used by
  both Re-check and Apply.
- **Day Care / Nursery** ⚠️ (written, **not yet compiled**) `SavePanel` renders a third slot row
  ("Nursery" on Gen 7+, "Day Care" below that) between the party row and the box grid. Driven by
  `Sav is IDaycareStorage` so it lights up for **every** generation that implements it (Gen 1–8,
  incl. SM/USUM via `SAV7`), and the whole `NurseryPanel` collapses when the save has no daycare
  block (e.g. `SAV7b`). Per slot: `dc.IsDaycareOccupied(i)` → `sav.GetStoredSlot(dc.GetDaycareSlot(i).Span)`
  (same call WinForms' `SlotInfoMisc.Read` makes), rendered as sprite + species + level (or "(egg)")
  + **nature** + legality badge; vacant slots show "(empty)". Nature shows on eggs too (the game
  hides it, but it's what you're checking when breeding with an Everstone). The nursery tile is
  taller than a box/party tile (106 vs 92) to fit the third label line. Clicking a slot puts the
  full nature label ("Adamant (+Atk, -SpA)") in the selection line. The header appends "egg available! 🥚" when
  `sav is IDaycareEggState { IsEggAvailable: true }`. **Read-only on purpose**: the row has its own
  `OnNurseryClick` handler rather than sharing `OnSlotClick`, so a daycare slot can never become the
  transfer/delete selection (`SelectedSlot` is indexed against box/party storage and would clobber
  the wrong slot). No double-tap editor either — writing back needs a `SetDaycareSlot`, which Core
  doesn't expose generically.
- **Two-save transfer** ✅ Two `SavePanel`s side by side; move/copy a Pokémon between saves via
  `EntityConverter` (handles cross-gen conversion; correctly refuses illegal backward transfers).
- **Legality** ✅ Per-slot legal/illegal badge + selection verdict via Core's `LegalityAnalysis`.
- **Gen 5 tools** ✅ (built + verified on a real save) A "🛠 BW/BW2 Tools" button appears on a
  `SavePanel` when `Sav is SAV5`, and opens `Gen5ToolsWindow` with a tab per available feature:
  - **Join Avenue** (BW2 only, `SAV5B2W2.JoinAvenue`) — full WinForms parity: Settings tab
    (name, title, experience, rank, ceiling color, flags, promotion, seed, 32-entry visiting-player
    TID/SID database), a Self tab, and list editors for Visitors, Occupants, Fans, Assistants. Each
    entity edits its General fields + type-specific fields (shop type/level/exp, shop counts, dex/
    medal, records, trivia, activities, all flags). Built in code via the `FieldForm` helper.
  - **Memory Link** (all `SAV5`, `Link1Data`/`Link2Data`) — import/export of the two raw 0x400-byte
    `.ml5` blocks via the native file picker (`StorageProvider.Open/SaveFilePickerAsync`, re-enabled
    on Avalonia 12.0.5 — see gotcha #3), with length validation.
  - **Global Link** (all `SAV5`, `sav.GlobalLink` → `GlobalLink5`) — Dream World / Game Sync block.
    A `FieldForm` editing account/sync fields (Upload Count, Upload Status, Slot Present, Registered,
    Account Full Access), downloaded content (Musical, C-Gear Skin, Dex Skin), the 20-slot item list
    (IDs + quantities as comma-separated text fields), and the 5 Dream World furniture slots (per-slot
    value + name) plus the selected-furniture index and sync flag. `UploadDate` is read-only in Core
    (no setter), so it's omitted. Furniture writes use `var fr = gl.GetFurniture(i); fr.X = …` — correct
    even though `DreamFurniture5` is a struct, because it wraps the shared `Memory<byte>`.
  - **Entrée Forest** (all `SAV5`, `sav.EntreeForest` → `EntreeForest`, plus `sav.Entralink`) — the
    Entralink levels/powers + Entrée Forest block. A settings `FieldForm` (White Forest / Black City
    levels; BW `MissionsComplete` or BW2 `PassPower1/2/3`; 9th-area unlock + extra-areas-unlocked
    0–6) with **Unlock All Areas** / **Delete All Slots** buttons, beside a 530-entry `ListBox` slot
    editor. Each `EntreeSlot` edits Species (name dropdown, incl. "(none)"=0), Special Move (name
    dropdown), Gender (0–3), Form (0–63) and Animation (`EntreeForestAnimation` enum dropdown). The
    `Slots` getter decrypts the block in place; Core re-encrypts on save via `EntreeForest.EndAccess()`
    (called from `SAV5.GetFinalData()`), so no manual crypto. Delete All routes through `_entreeReload`
    which drops the stale editor state before reloading, so the bulk-zero isn't overwritten on Apply.
  Edits apply to the live save in memory; the panel's 💾 Save (or the window's "Save to Disk")
  persists. The Join Avenue **date fields are now `DatePicker`s** (`JoinAvenueDate5.Date` `DateOnly?` ↔
  picker + Clear button) and **Version/Language are game/language dropdowns** (`FilteredGameDataSource(sav).Games`
  / `GameInfo.LanguageDataSource(5, EntityContext.Gen5)`). `FieldForm.Combo` no longer falls back to the
  first item when the stored value is out of range — it leaves the box blank so Save() preserves the
  original byte. Remaining minor deviations from WinForms: the records/trivia/activities/shop-count and
  activity-dates arrays still use comma-separated text fields.

- **Gen 4 tools / Pokéathlon** ⚠️ (written, **not yet compiled or run against a save**) A `Gen4 Tools`
  button appears on a `SavePanel` when `Sav is SAV4HGSS` (Pokéathlon is HG/SS only — DP/Pt have no
  such block), labelled "🛠 HG/SS Tools", and opens `Gen4ToolsWindow`, covering the whole `Pokeathlon4` block
  (`SAV4HGSS.Pokeathlon`, 0xD9D4, 0xB80). Same shape as the Gen 5/6 windows: live in-memory edits,
  `FieldForm` surfaces built in code-behind, one `_applyHooks` list flushed by Apply/Close,
  `ComboWheelGuard.Attach(this)`. Tabs:
  - **Pokéathlon** — accumulated `Points` (≤99,999), the 27 `DataCard4` flags as checkboxes labelled
    with their Gen 4 item name (item id **505** = "Data Card 01") *and* the stat each card reads out,
    the 12 once-daily Athlete Shop purchase flags, obtain-all / clear buttons, and a read-only
    summary (`CalculateGlobalScore`, `CalculateFriendshipTrophyCount`, `GetFameLevel`) with a
    Recalculate button — the summary is derived, so it only reflects what's already committed.
  - **Medals** — the 493-species × 5-bit medal bitfield as a species `ListBox` (each row shows a
    compact `SP·T·` tag string) driving 5 per-course checkboxes, plus Give All / Clear All
    (`SetAllMedals` / `Clear`), which route through the pane's reload so the stale form can't
    overwrite the bulk edit.
  - **Courses** — the 5 `PokeathlonCourseRecord4`s: three run scores + `ScoreMax` (the one summed
    into the global score) and the three `PokeathlonParticipant4`s (species dropdown, form, gender,
    shiny, encryption constant as hex, TID16/SID16).
  - **Best Scores** — `GetBestScore`/`SetBestScore` for all 10 events.
  - **Counters** — the whole `PokeathlonGlobalCounters4` block in two columns: play/performance
    counters, the wireless counters, the 10 per-event 1st-place tallies, `TotalEventLast`, `Fame`,
    and a read-only `TotalEventFirst` (not stored — Core sums the per-event counters).
  - **Events** / **Connections** — the player's own per-event data (`GetEventSelf`) and the wireless
    data (`GetEventConnection`): attempts plus the 5 stored records, each with its team of three
    packed `SpeciesForm10` entries; the Connections tab adds the 5 opposing
    `PokeathlonEventTrainer4`s (language dropdown, OT name, TID16/SID16).

  Deviations from the WinForms `SAV_Pokeathlon4`: no species sprites (the medal list is text-only, and
  participants have no sprite preview — avoids the `SpriteUtil` singleton in a second window), and
  form fields are numeric spinners rather than `FormConverter` name dropdowns (a form list depends on
  the selected species, which `FieldForm` has no dependent-combo support for). The Aprijuice block
  (`Aprijuice4`) is **not** included — Core has the wrapper but `SAV4HGSS` exposes no accessor for it.

- **Gen 6 tools** ⚠️ (written, **not yet compiled or run against a save**) An "🛠 XY/ORAS Tools"
  button appears on a `SavePanel` when `Sav is SAV6`, and opens `Gen6ToolsWindow`. Follows the Gen 5
  pattern exactly: live in-memory edits, `FieldForm` field surfaces built in code-behind, one
  `_applyHooks` list flushed by Apply/Close, `ComboWheelGuard.Attach(this)`. Tabs:
  - **Trainer** (all `SAV6`, incl. the ORAS demo) — OT/gender/TID/SID, game + language dropdowns,
    country/subregion/3DS console region, Game Sync ID, money/badges/BP/Vivillon, Poké Miles
    (records **63** current / **64** total), play time + seconds-to-start/fame, read-only last-saved
    stamp, the 5 PSS sayings, Mega Evolution unlock (+ Mega Rayquaza on `SAV6AO` only), the 20
    Battle Maison stats as 5 styles × normal/super × current/record, and on XY the trainer
    nickname + eye colour.
  - **Poké Puffs** (`ISaveBlock6Main`) — all 100 slots as name dropdowns + `PuffCount`, with Give All
    (varied / best), Reset, Sort and Sort-reverse.
  - **O-Powers** — all 65 `OPower6Index` unlock flags as checkboxes, `Points`, and the 10 field ×2 +
    7 battle ×2 power levels (0–3), plus Unlock All / Clear All.
  - **Super Training** — 32 regimen + 6 distribution unlock flags, `Counter`, `TutorialIndex`, the 12
    training-bag slots as dropdowns, and a 48-entry stage list whose editor covers both record times
    (float, via a text row — `FieldForm.Num` is integral) and both record holders
    (species/form/gender). Unlock All Stages, Unlock All + Distribution, Clear Block, Clear This Stage.
  - **Pokémon Link** — the `PL6` block: enabled flag, origin text, BP, Poké Miles, the 6 item +
    quantity pairs, the 6 gift entities (species/level/form/is-egg), raw `.pl6` import/export via the
    native picker, and `RefreshChecksum()` wired into both Apply and the export path.
  - **Hall of Fame** — 16 entries × 6 members. Entry pane edits `HallFame6Index` (clear index, Y/M/D,
    has-data); member pane edits species/form/gender/level/held item/4 moves/shiny/nicknamed/
    nickname/OT + OT gender/TID/SID/encryption constant. Delete Entry does the game's shift-down;
    Refresh Labels re-reads the two list panes.

  Known deviations from the WinForms Gen 6 editors: form fields are numeric spinners rather than
  `FormConverter` name dropdowns; the Trainer tab omits the 200-record trainer-stat grid, the
  multiplayer/PSS sprite picker and the `TrainerFashion6` appearance surface. The XY-only
  (Berry Field, Roamer, Friend Safari, accessory unlock, Pokédex) and ORAS-only (Secret Base,
  Pokéblocks) features are **not** included.

- **Let's Go Pikachu/Eevee (`SAV7b`)** ⚠️ (written, **not yet compiled or run against a save**) Boxes,
  party and a Trainer tool window. LGPE storage is unusual — 1000 slots stored as a **packed list**
  (Core pages it as 40 "boxes" × 25), **party-format data in the boxes**, and the party is 6 **pointers
  into box storage** (`PokeListHeader.PokeListInfo`; `HasParty` is false, but `PartyCount` /
  `GetPartyOffset` / `PartyData` work). What `SavePanel` does about it:
  - Box grid columns follow `BoxSlotCount` (`UpdateBoxColumns`: 6 for 30, 5 for 25/20) so rows fill.
  - Box tiles carry a `GetSlotTag` suffix from `GetBoxSlotFlags`: "(party n)", "★" starter, "🔒" locked
    — so a box Pokémon that is also in the party row is labelled.
  - `IsSlotProtected` = Core's `IsOverwriteProtected()` **plus** party membership (Core doesn't treat
    party as protected, and blanking an LGPE party-backed box slot leaves a dangling pointer). Used by
    `TryGetEmptySlot` and `ClearSelectedSlot`.
  - `CanClearSelectedSlot(out reason)` refuses an LGPE **party** move-out (Core's `DeletePartySlot`
    shifts the pointed-at box *data*, not the pointers) and protected box slots. `MainWindow.Move`
    checks it **before** copying, so a refused move doesn't silently become a copy.
  - Edits on SAV7b box slots also call `ResetPartyStats()` (boxes hold party-format data and Core's
    `SetPartyValues` skips the recompute when stats are present). Party edits write through the pointer.
  - `SaveCurrentToDiskAsync` calls `IStorageCleanup.FixStoragePreWrite()` (→ `PokeListHeader.CompressStorage`,
    closes gaps + re-points party/starter) before `Write()` and re-renders — the same step WinForms'
    `ExportSaveFile` runs. Without it a transfer into a later box leaves an illegal hole in the list.
  - `EditPokemonWindow` relabels the EV column "AV (0-200)" and reads/writes `IAwakened.AV_*` for PB7
    (LGPE has no EVs; non-zero EVs are illegal).
  - `LgpeToolsWindow` ("🛠 Let's Go Tools", `sav is SAV7b`) — Trainer tab only: OT + rival name
    (written only when changed, to avoid trash-byte churn), gender, game (GP/GE only), language
    (`SAV7b.Language` writes both copies), 6-digit TID / 4-digit SID (committed together via
    `SetTrainerID7` after `IsValidTrainerID7` — Core silently wraps an overflowing pair; the window
    throws into `Flush()` instead), Game Sync ID (16 hex, validated), money, play time, adventure-start
    date (`PlayerGeoLocation.AdventureBegin`, Epoch-1970 unsigned — pre-1970 rejected) and last-saved
    date (`Played.LastSavedDate`), both preserving their time of day. **Omitted vs WinForms
    `SAV_Trainer7GG`**: map coordinates, GO Park import/export, title/fashion unlocks. Capture Records
    and the Pokédex editor aren't ported either.

Deferred / not done: **compiling + testing the Gen 6 window** (see gotcha #7); sprite pixel-diff
verification vs a Windows build; CI; the remaining WinForms screens (other per-generation save
editors, etc.).

---

## Projects added by this port

- **`PKHeX.Avalonia/`** — the new GUI app. Run target. Key files:
  - `Program.cs`, `App.axaml(.cs)` — Avalonia bootstrap.
  - `MainWindow.axaml(.cs)` — currently the two-save transfer view (hosts two `SavePanel`s + move controls).
  - `SavePanel.axaml(.cs)` — reusable `UserControl`: one save's path/load, trainer line, box nav,
    clickable box grid, clickable party row (reads `sav.PartyData`, rendered above the box grid),
    wallpaper, per-slot legality badges, save-to-disk. Both grids feed one `OnSlotClick`; selection
    tracks `SelectedFromParty` so `ClearSelectedSlot` does a party-aware delete
    (`Sav.DeletePartySlot`) vs blanking a box slot. Exposes `Sav`, `SelectedPk`, `SelectedSlot`,
    `SelectedFromParty`, `CurrentBox`, `SelectionChanged`, `TryGetEmptySlot`, `ClearSelectedSlot`,
    `Refresh()`. Also hosts the per-generation tool buttons, which are **built dynamically after a
    save loads** rather than declared in XAML and greyed out — one disabled button per supported
    generation kept growing the nav row until the applicable one scrolled out of view. The XAML
    carries an empty `ToolsPanel` `StackPanel`; `LoadFromPath` calls `BuildToolButtons()`, which walks
    the static `ToolWindows` table and materialises a button only for the entries whose `Match`
    accepts the loaded save. Each entry is a `ToolWindow(Label, Match, Create)` record — labelled by
    games, not generation number ("HG/SS Tools", not "Gen4 Tools", since Pokéathlon is HG/SS only) —
    and `Create` receives the live save, `LoadedPath` and `SaveCurrentToDiskAsync` as the persist
    callback. `OpenToolWindow` re-checks `Match` at click time (the panel can load a different save
    after the button was built). **Adding another generation is one entry in `ToolWindows`** — no
    XAML, no field, no `FindControl`, no click handler; `MainWindow` needs no change.
  - `SpriteConvert.cs` — `SKBitmap → Avalonia Bitmap` helper + the `SlotVm` view-model.
  - `EditPokemonWindow.axaml(.cs)` — rich single-Pokémon editor opened by double-clicking a slot.
    Edits a clone (nickname, level, nature, ability, held item, 4 moves + per-move PP-Ups, IVs/EVs,
    gender/form/ball/friendship, OT/met data with name dropdowns + met-date picker, markings,
    ribbons, make-shiny) and hands the edited clone back via an `Action<PKM>` callback. XAML is a
    full named-control layout; name↔id lookups + ability/form/location/ball options built in
    code-behind. Markings + ribbons are built into two `FieldForm`s (reused from `Gen5Forms.cs`,
    along with the `Opt` combo record). One `WriteControlsToPk()` (shared by the Re-check preview and
    Apply) writes every control back to the clone; `LoadFromPk()` reflects the clone into the
    controls; `RefreshLegality()` shows `LegalityAnalysis.Report()`.
  - `Gen4ToolsWindow.axaml(.cs)` — the Gen 4 (HG/SS) feature window: the whole Pokéathlon block.
    Minimal XAML shell (`RootTabs` + Apply/Close + status text), everything else code-behind. Opened
    from `SavePanel.OpenGen4Tools()` with the live `SaveFile`, the loaded path and
    `SavePanel.SaveCurrentToDiskAsync`. Has one piece the Gen 5/6 windows don't: a reusable
    `BuildListPane(labels, build, hint, extra, listWidth)` helper — a left index `ListBox` driving a
    right-hand `FieldForm` built on demand, committing the open form on selection change and on
    Apply, and handing `extra` a reload callback that drops the stale form (the gotcha #9 fix). All
    four list-style tabs (Medals, Courses, Events, Connections) share it. Its `Flush()` also
    **returns false and keeps the window open** if a setter threw, instead of letting the exception
    escape the `async void` click handler (gotcha #8).
  - `ToolsUi.cs` — the shared layout/option helpers for the tool windows: `TwoColumns`, `WithToolbar`,
    `Bulk`, `ReadOnlyRow`, `HexRow`, `ToOpts`, `ToOptsWithNone`, `SplitPascal`. These began as private
    statics inside `Gen6ToolsWindow`, which **still has its own copies** — anything new should use
    `ToolsUi`, and Gen6 should be migrated to it when it's next touched.
  - `Gen5ToolsWindow.axaml(.cs)` — the Gen 5 feature window (Join Avenue + Memory Link + Global Link +
    Entrée Forest). XAML is a minimal shell (`RootTabs` TabControl + Apply/Close buttons); all field UI
    is built in code-behind. Opened from `SavePanel.OpenGen5Tools()`; receives the live `SaveFile`, the
    loaded path, and a persist callback (`SavePanel.SaveCurrentToDisk`).
  - `Gen6ToolsWindow.axaml(.cs)` — the Gen 6 feature window (Trainer + Poké Puffs + O-Powers + Super
    Training + Pokémon Link + Hall of Fame). Same shape as `Gen5ToolsWindow`: minimal XAML shell
    (`RootTabs` + Apply/Close + status text), everything else code-behind. Opened from
    `SavePanel.OpenGen6Tools()` with the live `SaveFile`, the loaded path, and
    `SavePanel.SaveCurrentToDiskAsync` as the persist callback. Species/move/item dropdowns come from
    `new FilteredGameDataSource(sav, GameInfo.Sources)` (**not** the raw `GameStrings` arrays, which
    run to Gen 9 and would let you write an out-of-range id into a Gen 6 block); Gen6-only tables
    (`puffs`, `trainingbags`, `trainingstage`) come straight from `GameInfo.GetStrings("en")`.
  - `LgpeToolsWindow.axaml(.cs)` — the Let's Go (`SAV7b`) tool window: a Trainer tab. Same shell
    and `_applyHooks` / guarded `Flush()` shape as `Gen4ToolsWindow`.
  - `NatureDisplay.cs` — shared nature formatting. `GetName(nature)` → "Adamant";
    `GetLabel(nature)` → "Adamant (+Atk, -SpA)" (or "(neutral)") via `NatureAmp.GetNatureModification`;
    `IsRealNature(nature)` filters the 25 real natures out of the `Nature` enum (which also carries
    Random / out-of-range values). Used by `EditPokemonWindow`'s nature dropdown and `SavePanel`'s
    nursery row — don't re-derive the +/- stat labels anywhere else.
  - `Gen5Forms.cs` — `FieldForm` (code-built label/value form with auto Load/Save) + `Opt` combo item.
    Rows: `Num`/`Text`/`Check`/`Combo`/`Custom`/`CustomControl` plus `Date` (a `DatePicker` + Clear
    button wired in `DateOnly?` terms). Used to assemble the large Join Avenue + Entrée field surfaces
    without hand-written XAML + FindControl. Despite the name it is **generation-agnostic** — the
    Pokémon editor and the Gen 6 window both build on it. `Num` is integral (`FormatString = "0"`), so
    float fields (e.g. Super Training record times) need a `Custom` text row instead.
- **`PKHeX.SpriteTest/`** — headless console harness. Loads a save, builds sprites, writes PNGs.
  Use it to verify the drawing pipeline **without Avalonia** (isolates Core/Skia bugs from UI bugs).
  `dotnet run --project PKHeX.SpriteTest -- "/path/to/save"`
- **`Porting/`** — `SYSTEM_DRAWING_TO_SKIASHARP.md` (the migration map) and `PORTING_NOTES.md`
  (the gotchas). Read both.

Both new projects are registered in `PKHeX.slnx`.

---

## Build / run / test (macOS)

```bash
# Run the app
dotnet run --project PKHeX.Avalonia/PKHeX.Avalonia.csproj

# Build individual projects (do NOT build PKHeX.slnx whole — it includes WinForms, which fails off Windows)
dotnet build PKHeX.Drawing.PokeSprite/PKHeX.Drawing.PokeSprite.csproj

# Core test suite
dotnet test Tests/PKHeX.Core.Tests/PKHeX.Core.Tests.csproj

# Sprite pipeline sanity (no UI)
dotnet run --project PKHeX.SpriteTest -- "/path/to/your.sav"
```

Requires the .NET 10 SDK. `PKHeX.WinForms` cannot build on macOS/Linux — that's expected.

---

## CRITICAL gotchas (see Porting/PORTING_NOTES.md for detail)

1. **SkiaSharp version must be ONE value everywhere.** Avalonia 12.0.5 resolves
   `SkiaSharp 3.119.4`. The drawing projects are pinned to that exact version. A
   mismatch (e.g. a drawing DLL built against 3.116 running on a 3.119 native lib) = **silent
   native segfault**, no managed exception. If you bump Avalonia, re-check with
   `dotnet list PKHeX.Avalonia/PKHeX.Avalonia.csproj package --include-transitive | grep -i skia`
   and realign `PKHeX.Drawing.csproj` + `PKHeX.Drawing.Misc.csproj`.
2. **Decoded sprites must be forced to `Bgra8888`.** `SKBitmap.Decode` returns `Rgba8888` by
   default on macOS; `ImageUtil`'s pixel math assumes BGRA. Without conversion, *tinted* sprites
   (shiny/tera/encounter) get red/blue swapped. The resource facades already do this — any new
   `SKBitmap.Decode` call must too.
3. **Native macOS file picker** — the `StorageProvider.OpenFilePickerAsync` →
   `libAvaloniaNative.dylib` segfault was fixed upstream in **Avalonia 12.0.1** (we now target
   12.0.5), so the native dialog is safe to use again. The existing path-TextBox workarounds (e.g.
   Memory Link import/export in `Gen5ToolsWindow`) still work but can be migrated back to the native
   picker when convenient.
4. **Avalonia 12 API renames** that bit us: `TextBox.Watermark` → `PlaceholderText`;
   `DataFormats`/`e.Data` drag API changed (`DataFormats` → `DataFormat`); `NumericUpDown.Value`
   is `decimal?`.
5. **Avalonia `ComboBox` changes selection on mouse-wheel while closed.** In a scrollable form,
   scrolling with the pointer over a combo silently edits it (e.g. ability "Slot 2" → "Hidden"),
   and the next Apply/Re-check persists the accident. `ComboWheelGuard.Attach(window)` (tunnel
   handler; swallows the wheel and scrolls the ancestor `ScrollViewer` instead) is applied to
   `EditPokemonWindow` + `Gen4ToolsWindow` + `Gen5ToolsWindow` + `Gen6ToolsWindow` + `LgpeToolsWindow` — attach it to any new window
   containing combos.
6. **`SpriteUtil` is a global singleton.** `SpriteUtil.Initialize(sav)` sets the active sprite
   builder. With two saves loaded, call `Initialize(thisSav)` at the START of each panel's render
   so sprites use the right builder. `SavePanel.RenderBox` already does this.
7. **`Gen4ToolsWindow`, `Gen6ToolsWindow` and `LgpeToolsWindow` (+ the SAV7b changes in `SavePanel` /
   `EditPokemonWindow` / `MainWindow`) have never been compiled.** For LGPE, verify on a real save:
   transfer into box 2+ → save → reload (storage compaction), edit a party member, and that a party
   member's box tile shows "(party n)". For Gen 4 specifically, all
   the Pokéathlon block wrappers (`PokeathlonMedalManager4`, `PokeathlonCourseRecord4`,
   `PokeathlonEventData4`, `PokeathlonParticipant4`, `PokeathlonGlobalCounters4`, …) are plain
   `struct`s over a `Memory<byte>` — unlike Gen 6's `HallFame6Entity` ref structs they **can** be
   captured by a lambda and mutated through the copy, which is what every `FieldForm` row here does.
   The one exception is `SpeciesForm10`: it's a packed value, *not* a view over save memory, so those
   rows read-modify-write the whole field (see `AddEntryRows`). Verify round-trips on a real HG *and*
   SS save.
8. **`Gen6ToolsWindow` has never been compiled.** It was written against Core by reading the source,
   not by building — expect a tail of name/signature fixes on the first
   `dotnet build PKHeX.Avalonia/PKHeX.Avalonia.csproj`, and verify round-trips (edit → save → reload)
   on a real XY *and* ORAS save before trusting it.
9. **Apply runs from an `async void` click handler.** `apply.Click += async (_, _) => …` in the tools
   windows means an exception escaping any `FieldForm.Save` hook is unhandled and kills the process.
   Setters that validate (notably `MyStatus6.GameSyncID`, which throws on both a wrong length and a
   non-hex digit) must be guarded in the UI layer, not left to throw. See
   `Gen6ToolsWindow.SetGameSyncID`.
10. **A `FieldForm`'s registered `Save()` will happily undo a bulk edit.** The rows captured the
   pre-op values when they were loaded, so after any block-level mutation (`Puff6.MaxCheat`,
   `OPower6.ClearAll`, `SuperTrainBlock.ClearBlock`, `HallOfFame6.ClearEntry`, `EntreeForest.DeleteAll`)
   you must either call `form.Load()` to re-read, or drop the cached form entirely before the next
   Apply. Both patterns are in `Gen6ToolsWindow` (`Bulk(...)` for the simple case, `_superTrainReload`
   / `_fameReload` for the list editors).

---

## Established conventions

- **Avalonia code-behind, not generated fields.** Constructors call `AvaloniaXamlLoader.Load(this)`
  then resolve controls with `this.FindControl<T>("Name")!`. (A hand-written `InitializeComponent`
  shadows Avalonia's generated one and leaves named fields null — avoid it.)
- **Reflection bindings** (`AvaloniaUseCompiledBindingsByDefault=false`) so `DataTemplate`s don't
  need `x:DataType`. View-models are plain classes (e.g. `SlotVm`).
- **Sprites to UI:** `SpriteConvert.ToAvalonia(skBitmap)` PNG-encodes then loads an Avalonia
  `Bitmap`. Simple and correct; switch to `WriteableBitmap` only if perf demands.
- **Reusable per-save UI** lives in `SavePanel`; compose multiples in `MainWindow`.

---

## PKHeX.Core API cheat-sheet (verified during this work)

Loading / saving:
- `SaveUtil.GetSaveFile(path)` → `SaveFile?`
- `sav.Write()` → `Memory<byte>` (`.ToArray()` then `File.WriteAllBytes`)
- `sav.BoxCount`, `sav.GetBoxData(box)` → `PKM[]`, `sav.SetBoxSlotAtIndex(pk, box, slot)`
- `sav.PartyData` → `IList<PKM>`, `sav.BlankPKM`, `sav.PKMType`
- `sav.IsBoxSlotOverwriteProtected(box, slot)`
- `(sav as IBoxDetailNameRead)?.GetBoxName(box)` — box names (not all saves implement it)

Trainer (on `SaveFile`): `OT`, `Gender` (1=♀ else ♂), `DisplayTID`/`DisplaySID`, `Version`
(`GameVersion`), `Money`, `PlayedHours`/`PlayedMinutes`.

Sprites / strings (drawing layer):
- `SpriteUtil.Initialize(sav)` then `pk.Sprite()` → `SKBitmap` (needs `using PKHeX.Drawing.PokeSprite`)
- `sav.WallpaperImage(box)` and `sav.Sprite()` (trainer) — `using PKHeX.Drawing.Misc`
- `SpriteUtil.GetLegalIndicator(bool valid)` → badge `SKBitmap`
- `SpeciesName.GetSpeciesName(species, language)` (English = 2)
- `GameInfo.GetStrings("en")` → `GameStrings` with `.itemlist`, `.movelist`, `.abilitylist`
  (string[] **indexed by ID**)

PKM editing:
- `pk.Clone()` → `PKM` (edit a clone so Cancel discards). `pk.RefreshChecksum()` before writing back.
- `pk.Nickname` / `pk.IsNicknamed`, `pk.CurrentLevel` (byte; sets EXP), `pk.Nature` (`Nature` enum)
- `pk.HeldItem` (int id), `pk.Move1..Move4` (ushort id), `pk.Ability` (id, read),
  `pk.RefreshAbility(slotIndex 0/1/2)` to set ability by slot, `pk.AbilityNumber` (1/2/4 bitflag;
  current slot = `BitOperations.TrailingZeroCount`). Ability options for a species:
  `pk.PersonalInfo.AbilityCount` + `GetAbilityAtIndex(n)` (and `GetIndexOfAbility(id)`).
- Moves PP: `pk.Move1_PPUps..Move4_PPUps` (int 0–3). Set moves + PP-Ups, then `pk.FixMoves()`
  (reorders empties to the end, carries PP/PP-Ups along, zeroes empties) then `pk.HealPP()`
  (current PP → max for each move's PP-Ups). For party write-back also call `pk.ResetPartyStats()`.
- IV/EV order is **HP, ATK, DEF, SPE, SPA, SPD**: `pk.IV_HP/IV_ATK/IV_DEF/IV_SPE/IV_SPA/IV_SPD`
  (0–31), `pk.EV_*` (0–252). Speed is the 4th, before the special stats.
- `pk.Gender`/`pk.Form`/`pk.Ball`/`pk.CurrentFriendship` (all byte), `pk.OriginalTrainerName` (string),
  `pk.OriginalTrainerGender` (byte), `pk.DisplayTID`/`DisplaySID` (uint), `pk.MetLevel` (byte),
  `pk.MetLocation` (ushort), `pk.MetDate` (`DateOnly?`; null = unset / unsupported format).
- `pk.SetShiny()`.
- PID: `pk.PID` (uint, shown as X8 hex). **Gen3–5 derive nature (Gen3/4), gender, and the non-hidden
  ability slot from the PID** (`pk.PIDAbility`; -1 when not PID-derived). Plain setters for those are
  no-ops or revert on reload — embed changes via `pk.SetPIDNature(nature)` (Format 3/4),
  `pk.SetPIDGender(gender)` (Format ≤5), and `pk.SetAbilityIndex(slot)` (Format ≤5, slot <2, only
  when `slot != pk.PIDAbility` — it rerolls unconditionally otherwise). All of these reroll the PID
  (until non-shiny for the first two) while preserving the other PID-derived traits, and sync
  `EncryptionConstant = PID` where needed. Manual PID edits on Gen3–5 mons in Format ≥6 must also set
  `EncryptionConstant = PID`. `pk.SetPIDGender(pk.Gender)` = the "reroll PID" button.
- Form names: `FormConverter.GetFormList(species, strings.types, strings.forms,
  GameInfo.GenderSymbolUnicode, pk.Context)` → `string[]` indexed by form id.
- Met-location dropdown: `GameInfo.GetLocationList(pk.Version, pk.Context, egg)` → `IReadOnlyList<ComboItem>`
  (`ComboItem(string Text, int Value)`). A location id may not be in the list (transferred mons) — add a
  synthetic entry so it round-trips.
- Ball names: `GameInfo.GetStrings("en").balllist` (string[] indexed by ball id).
- Markings: cast `pk` to the generation interface — `IAppliedMarkings3` (Gen3, bool, 4 shapes),
  `IAppliedMarkings4` (Gen4–6, bool, 6 shapes), `IAppliedMarkings7` (Gen7+, `MarkingColor`
  None/Blue/Pink, 6 shapes). Named props `MarkingCircle/Triangle/Square/Heart/Star/Diamond`.
- Ribbons: `RibbonInfo.GetRibbonInfo(pk)` → `List<RibbonInfo>` (`Name`, `Type` Boolean/Byte,
  `MaxCount`). Read/write the live value with `ReflectUtil.GetValue(pk, name)` /
  `ReflectUtil.SetValue(pk, name, value)`. (Reflection carries `[RequiresUnreferencedCode]`; the
  Avalonia app isn't trimmed, so suppress IL2026 on the calling method.)

Gen 4 saves (HG/SS) — Pokéathlon:
- Hierarchy `SaveFile → SAV4 → {SAV4DP, SAV4Pt, SAV4HGSS}`. The Pokéathlon block is **HG/SS only**:
  `sav.Pokeathlon` → `Pokeathlon4` (a sealed **class** over `GeneralBuffer.Slice(0xD9D4, 0xB80)`), so
  gate on `sav is SAV4HGSS`. Everything below is a `struct` over a `Memory<byte>` slice of the live
  save — freely capturable by a lambda, and mutating a captured copy writes through (no copy-back).
- `Pokeathlon4`: `Points` (`MaxPoints` 99,999), `FlagsDataCard` (27 bits, `DataCardAllObtained`),
  `FlagsDailyShop` (12 bits, `FlagsShopAllObtained`), `GetBestScore`/`SetBestScore(PokeathlonEvent4)`
  (the per-event Athlete-Point bests), `CalculateGlobalScore()` (course bests + medal count + event
  bests), `CalculateFriendshipTrophyCount(score)` + `FriendshipTrophyThresholds` (10 max).
- `PokeathlonStat4` = the 5 courses (Speed/Power/Skill/Stamina/Jump, `.Count`);
  `PokeathlonEvent4` = the 10 events (`.Count`); `DataCard4` = the 27 cards, each naming the counter
  it reads out. Data Card item names: `GameStrings.GetItemStrings(EntityContext.Gen4)` from item id
  **505** ("Data Card 01").
- `GetCourseRecord(PokeathlonStat4)` → `PokeathlonCourseRecord4`: `Score0/1/2` + `ScoreMax` (only
  `ScoreMax` feeds the global score), `GetParticipant(0..2)` → `PokeathlonParticipant4` (packed
  `Species`/`Form`/`Gender`/`IsShiny` + `EncryptionConstant` + `ID32`/`TID16`/`SID16`).
- `Medals` → `PokeathlonMedalManager4`: `SIZE` = 493 (1-indexed `[Bulbasaur..Arceus]`);
  `GetMedal`/`SetMedal`/`AwardMedal(species, bits)` with 5 bits per species indexed by
  `PokeathlonStat4` (`MaxMedalBits` 0b11111), `SetAllMedals()`, `Clear()`, `GetTotalCount()`.
  **Species is 1-indexed**: `GetMedal` decrements first, so species 0 underflows and throws.
- `GetEventSelf(PokeathlonEvent4)` → `PokeathlonEventData4`: `Attempts` (`MaxAttempts`) +
  `GetRecord(0..4)` (`MaxRecord` = 5) → `PokeathlonEventRecord4` (`Record`, units are event-specific
  — Hurdle Dash is a frame count, lower is better — plus `Entry0/1/2`).
- `GetEventConnection(PokeathlonEvent4)` → `PokeathlonConnection4`: `.Inner` (a `PokeathlonEventData4`
  at offset 0) + `GetTrainer(0..4)` → `PokeathlonEventTrainer4` (`OriginalTrainerName` (7 chars,
  encoded per `Language` — set Language first), `TID16`/`SID16`, `Language`).
- `SpeciesForm10` — a packed `ushort` (10 bits species, 6 bits form) with implicit `ushort` conversions
  and an `IsValid` sanity check. **Not** a view over save memory: edit it read-modify-write
  (`var e = rec.Entry0; e.Species = x; rec.Entry0 = e;`).
- `GlobalCounters` → `PokeathlonGlobalCounters4`: `TimeSpent` (minutes, `MaxPlay` 59,999),
  `SessionsJoined`, `PlacedFirst`/`PlacedLast`, `BonusesEarned`, `Instructions`, `Failed`, `Jumped`,
  `Acquired`, `Tackled`, `FellDown`, `Dashed`, `Switched`, `SelfImpeded`, `ConnectionJoined/First/Last`,
  `this[PokeathlonEvent4]` (per-event 1st places), `TotalEventLast`, `Fame` (`MaxFame`), all clamped to
  `MaxStat` 9,999,999. `TotalEventFirst` is **computed** (summed from the indexer, not stored) and
  `SetDataCardStat(DataCard4.EventFirst, …)` is a deliberate no-op. `GetFameLevel(value)` →
  0–12 against `FameInclusiveThreshold`. `Get/SetDataCardStat(DataCard4)` maps a card to its counter.
- `Aprijuice4` exists in Core (Fame/Mildness/5 flavours, `CalculateLevel`, `CalculatePrice`) but
  **`SAV4HGSS` exposes no accessor for it** — nothing to wire up from the save side yet.

Gen 5 saves (BW/BW2):
- Hierarchy `SaveFile → SAV5 → {SAV5BW, SAV5B2W2}`. Detect with `sav is SAV5` (any Gen5) /
  `sav is SAV5B2W2` (BW2-only features). Both `Memory<byte>` blocks below edit the live buffer.
- **Memory Link** (on `SAV5`, BW + BW2): `sav.Link1Data`/`Link2Data` (`Memory<byte>`, 0x400 each);
  write with `sav.SetLink1Data(span)` / `SetLink2Data(span)`. Raw blobs (`.ml5`), no parsed fields.
- **Global Link** (on `SAV5`, BW + BW2): `sav.GlobalLink` → `GlobalLink5` (Dream World / Game Sync).
  `UploadDate` (`DateQuad5`, read-only), `UploadCount` (-1 if never), `UploadStatus`, flags
  `IsSlotPresent`/`IsRegistered`/`IsAccountFullAccess`, content bytes `Musical`/`CGearSkin`/`DexSkin`.
  Items: `CountItems`=20, `GetItem(i)`/`SetItem(i,ushort)` + `GetItemQuantity(i)`/`SetItemQuantity(i,byte)`.
  Furniture: `CountFurniture`=5, `GetFurniture(i)` → `DreamFurniture5` struct (`.Value` ushort, `.Name`)
  over shared memory; `SelectedFurnitureIndex` (0x7F=none), `IsFurnitureSynchronized`.
- **Join Avenue** (on `SAV5B2W2`, BW2-only): `sav.JoinAvenue` → `JoinAvenue5`. `.Settings`
  (`JoinAvenueSettings5`: Name, PlayerTitle, Experience, Rank≤9999, CeilingColor, promotion, 32-slot
  visiting-player DB), `.Self`, `GetVisitor(i)`/`GetOccupant(i)` → `JoinAvenueVisitor5`,
  `GetFan(i)` → `JoinAvenueFan5`, `GetAssistant(i)` → `JoinAvenueAssistant5`. Counts are consts on
  `JoinAvenue5` (`VisitorCount`, `FanCount`, `OccupantCount`, `AssistantCount`). Each getter returns
  a fresh wrapper over the SAME backing memory, so edits persist; no clone/copy-back needed (the
  WinForms editor clones + `CopyChangesFrom`, but the Avalonia panel edits the live save directly).
  Entities share `IJoinAvenueEntity5` (name/shout/greeting/met/seed/…); dates are `JoinAvenueDate5`
  (`.RawValue` packed `ushort`; use `.Date` `DateOnly?` for a friendly picker — null clears `RawValue`).
- **Entralink / Entrée Forest** (on `SAV5`, BW + BW2): `sav.Entralink` → `Entralink5` (abstract:
  `WhiteForestLevel`/`BlackCityLevel`; `Entralink5BW.MissionsComplete`; `Entralink5B2W2.PassPower1/2/3`).
  `sav.EntreeForest` → `EntreeForest`: `Slots` → `EntreeSlot[530]` (calling the getter decrypts the
  block in place; `EndAccess()`/`StartAccess()` toggle crypto, and `SAV5.GetFinalData()` calls
  `EndAccess()` so saving re-encrypts automatically — no manual crypto in UI). Each `EntreeSlot` has
  `Species`/`Move` (ushort), `Gender` (0–3), `Form` (0–63), `Animation` (`EntreeForestAnimation` enum),
  `Area` (read-only flags metadata), `Delete()`. Forest-level: `Unlock9thArea` (bool), `Unlock38Areas`
  (0–6), `UnlockAllAreas()`, `DeleteAll()`.
- Game/language dropdowns: `new FilteredGameDataSource(sav, GameInfo.Sources).Games` (versions valid
  for the loaded save) and `GameInfo.LanguageDataSource(5, EntityContext.Gen5)` → `IReadOnlyList<ComboItem>`.

Gen 6 saves (XY/ORAS):
- Hierarchy `SaveFile → SAV_BEEF → SAV6 → {SAV6XY, SAV6AO, SAV6AODemo}`. Detect with `sav is SAV6`
  (any Gen6) / `SAV6XY` / `SAV6AO`. **Feature gate on `ISaveBlock6Main`, not `SAV6`** — that interface
  is XY+ORAS retail only, so it correctly excludes `SAV6AODemo`, which has none of the feature blocks
  (`Puff`, `OPower`, `SuperTrain`, `Maison`, `Link`, `HallOfFame`, `BattleBox`, `Config`, `Encount`,
  `SUBE`, `MysteryGift`, `GTS`, `Fused`, `BoxLayout`).
- Block accessors on `SAV6XY`/`SAV6AO` are **get-only properties returning mutable wrappers** over
  `Memory<byte>` slices of the live save. The reference is read-only; the contents are read/write, and
  every getter returns a fresh view over the SAME bytes — so no clone/copy-back (same deal as Gen 5).
- Trainer block: `sav.Status` → `MyStatus6` (`SAV6XY.Status` covariantly returns `MyStatus6XY`).
  `Saying1..5` (17 chars each, `SAV6.LongStringLength / 2`), `IsMegaEvolutionUnlocked`,
  `IsMegaRayquazaUnlocked` (ORAS-only in practice), `GameSyncID` (**exactly 16 hex chars or it throws**
  twice over — `ArgumentOutOfRange` on length, `FormatException` on a non-hex digit; validate before
  assigning). XY extras on `MyStatus6XY`: `Nickname`, `EyeColor` (`short`, can be negative), `Fashion`
  → `TrainerFashion6`.
- On `SAV6` directly: `Money` (max `sav.MaxMoney` = 9999999), `Badges`, `BP`, `Vivillon` (all
  `abstract int`), `Region`/`Country`/`ConsoleRegion`, `GetRecord/SetRecord/GetRecordMax(int)`
  (`RecordBlock6`, 200 records — **63 = Poké Miles current, 64 = total earned**), `Played`
  (`PlayTime6`: `PlayedHours/Minutes/Seconds`, read-only `LastSavedTime` string, `LastSavedDate`
  `DateTime?`), `SecondsToStart`/`SecondsToFame`, `EventWork` (`EventWork6`), `Situation`, `Overworld`.
- **Poké Puffs** (`ISaveBlock6Main.Puff` → `Puff6`): `GetPuffs()` → `Span<byte>` (100 slots, ids 0–26),
  `SetPuffs(span)`, `PuffCount`, `Reset()` (default 5), `MaxCheat(bool special)`, `Sort(bool reverse)`.
  Names: `GameInfo.GetStrings("en").puffs`.
- **O-Powers** (`.OPower` → `OPower6`): `Points`, `GetState/SetState(OPower6Index, OPowerFlagState)`
  (65 indices, 0 = master Enable), `GetLevel1/2` + `SetLevel1/2` overloaded on **both**
  `OPower6FieldType` (10) and `OPower6BattleType` (7), levels 0–3. `UnlockAll()`, `ClearAll()` (which
  deliberately preserves the Enable flag).
- **Super Training** (`.SuperTrain` → `SuperTrainBlock`): `GetIsRegimenUnlocked/SetIsRegimenUnlocked(i)`
  (48 slots, first 32 released), `GetIsDistributionUnlocked/Set…(i)` (6), `Counter` (≤10),
  `TutorialIndex` (≤6), `GetTime1/2(i)` + `SetTime1/2(i, float)` (**float** — don't route through an
  integral spinner), `GetHolder1/2(i)` → `SuperTrainingSpeciesRecord` (a **class** over shared memory:
  `Species`/`Form`/`Gender`, `Clear()`, `LoadFrom(pk)`), `GetBag/SetBag(i)` (12),
  `AddBag`/`RemoveBag`/`GetOpenBagIndex`, `ClearRecord1/2(i)`, `UnlockAllStages(bool dist)`,
  `ClearBlock()`. Names: `.trainingstage` (32) and `.trainingbags` on `GameStrings`.
- **Pokémon Link** (`.Link` → `LinkBlock6`): `.Gifts` → `PL6` (a fresh wrapper over the same
  `Raw.Slice(0x1FF, PL6.Size)` each call, so cache it freely). `PL6.Size` = 0xA47 (`.pl6` file length),
  `Enabled` (bit 7 of `Flags`), `Origin` (54 chars), `BattlePoints`, `Pokemiles`, `Item1..6` +
  `Quantity1..6`, `Entity1..6` → `LinkEntity6` (a **class** view, not a copy). Raw block bytes are
  `link.PL6` (`Memory<byte>`); call `link.RefreshChecksum()` after any edit or import
  (CRC16-CCITT over `Data[0x200..^4]`), readable via `link.Checksum`.
- **Hall of Fame** (`.HallOfFame` → `HallOfFame6`): `Entries` = 16, `PokeCount` = 6,
  `GetEntry(i)` → `Span<byte>` (the trailing 4 bytes are the `HallFame6Index`), `GetEntity(team, member)`
  → `Span<byte>`, `ClearEntry(i)` (shifts later entries **down**, so every cached editor view is stale
  afterwards), `GetInsertIndex(out clear)`. Wrap the spans in `new HallFame6Entity(span, language)` /
  `new HallFame6Index(span)` — both are `readonly ref struct`s whose property setters write through to
  the save. **They can't be cached in a field or captured by a lambda**: re-wrap inside every
  getter/setter lambda (`new HallFame6Entity(fame.GetEntity(t, m), sav.Language).Species`), and to
  write, declare the local *inside* the lambda body (`var e = …; e.Species = v;`).
- **Battle Maison** (`.Maison` → `MaisonBlock`): `MaisonStatCount` = 20 =
  5 `BattleStyle6` × {normal, super} × {current, streak}. Use
  `Get/SetMaisonStat(BattleStyle6, bool streak, bool super)`; the flat index is
  `(style << 2) | (streak ? 2 : 0) | (super ? 1 : 0)`.
- ORAS-only / XY-only blocks (**not yet in the Avalonia UI**): `SAV6AO.SecretBase`
  (`SecretBase6Block`), `SAV6AO.Contest` (`Contest6`, 12 Pokéblock counts), `BerryField6AO`
  (typed, 90 plots, `ResetAndRandomize`), `SangoInfoBlock` (Eon Ticket / base QR, reachable only as
  `SAV6AO.Blocks.Sango`), `SAV6XY.BerryField` (`BerryField6XY`, 32 raw undocumented plots),
  `SAV6XY.Fashion.UnlockAllAccessories()`, `SAV6XY.UnlockAllFriendSafariSlots()`,
  `Encount6.Roamer`/`.Radar`, `Zukan6XY`/`Zukan6AO` (`GiveAll`), `SubEventLog6XY.ChateauRank/Points`,
  `SubEventLog6AO.SeasideCyclingRoad*`.
- Dropdowns: build species/move/item lists from `new FilteredGameDataSource(sav, GameInfo.Sources)`
  (`.Species`, `.Moves`, `.Items`, `.Games`) — the raw `GameStrings` arrays cover Gen 9 and let you
  write an out-of-range id into a Gen 6 block. Languages:
  `GameInfo.LanguageDataSource(6, EntityContext.Gen6)`. Note the filtered lists may omit value 0, which
  is the legitimate "empty" state for a HoF member / Super Training holder / Link gift — prepend it.

Day Care / Nursery (generation-agnostic):
- `sav is IDaycareStorage dc` — `dc.DaycareSlotCount`, `dc.IsDaycareOccupied(i)`,
  `dc.GetDaycareSlot(i)` → `Memory<byte>` over the live save, `dc.SetDaycareOccupied(i, bool)`.
  Decode with `sav.GetStoredSlot(span)` (**stored**, not party, format — even on Gen 5 where the
  slice is `SIZE_5PARTY`; `GetStoredSlot` takes the leading `SIZE_STORED` bytes and decrypts).
- `sav is IDaycareEggState` → `IsEggAvailable`; `IDaycareRandomState<T>` → `Seed` (Gen 7 = `UInt128`,
  and it's an **explicit** interface impl on `SAV7`, so you must cast). `IDaycareExperience` →
  `Get/SetDaycareEXP(i)` on the gens that store it.
- Gen 7 (`SAV7`, SM/USUM): `sav.Daycare` → `Daycare7`, 2 slots, slot stride
  `PokeCrypto.SIZE_6STORED + 1` (a leading occupancy byte per slot). LGPE (`SAV7b`) has a
  `Daycare7b` block but does **not** implement `IDaycareStorage` — the games have no breeding.
- Gen 8 SwSh uses `IDaycareMulti` (multiple daycares, indexer → `IDaycareStorage`), **not**
  `IDaycareStorage` directly, so an `is IDaycareStorage` gate silently skips it.

Transfer / legality:
- `EntityConverter.TryMakePKMCompatible(srcPk, destSav.BlankPKM, out result, out converted)` →
  `bool`; check `result.IsSuccess`. Handles cross-gen conversion. Transfers are **forward-only**
  by design — backward (e.g. Gen5→Gen3) returns `NoTransferRoute` and must be refused. The global
  `EntityConverter.AllowIncompatibleConversion` can force it but produces **illegal** Pokémon —
  leave it at the default `DisallowIncompatible`.
- `new LegalityAnalysis(pk).Valid` → bool (can throw on odd data — wrap in try/catch).

---

## How to add a feature (the workflow that worked)

1. **Find the Core API first** (grep `PKHeX.Core` for the property/method). Core already does the
   hard work; the UI just calls it.
2. **Wire it into the Avalonia UI** following the conventions above.
3. **Build the single project** and fix compile errors (I can't compile here — expect a short
   tail of name/signature fixes on first build).
4. **Test on a real save** and verify round-trips (edit → save → reload).
5. For anything sprite/Skia-related, sanity-check in `PKHeX.SpriteTest` first to rule out UI noise.

---

## Suggested roadmap

- **Reintroduce rich editing** ✅ Done — `EditPokemonWindow`, opened by double-clicking a slot,
  edits a clone alongside the transfer view. Covers PP-aware moves with per-move PP-Ups,
  gender/form/ball/friendship, OT/met data (form + met-location name dropdowns, met-date picker),
  markings, ribbons, and a live `LegalityAnalysis.Report()` panel with a Re-check button. The editor
  feature set is essentially complete; further entity work would be niche fields (e.g. contest stats,
  hyper-training, Gigantamax/Tera) as needed.
- **Gen 5 polish** ✅ — Join Avenue date fields are now `DatePicker`s and Version/Language are
  game/language dropdowns (`FilteredGameDataSource(sav).Games` / `GameInfo.LanguageDataSource(5, …)`)
  via the new `FieldForm.Date` row + the now-safe `FieldForm.Combo` (preserves out-of-range values).
  Remaining nicety: the Join Avenue records/trivia/activities/shop-count + activity-dates arrays are
  still comma-separated text. Other gens' misc editors can follow the same `Gen5ToolsWindow` +
  `FieldForm` pattern.
- **Entralink5 / Entrée Forest** ✅ — added as an "Entrée Forest" tab (all `SAV5`): Entralink
  levels/powers + forest unlock settings + Unlock-All / Delete-All buttons + a 530-slot `ListBox`
  editor (Species/Move/Gender/Form/Animation). Crypto handled by Core (`Slots` getter decrypts;
  `SAV5.GetFinalData()` → `EndAccess()` re-encrypts on save).
- **Gen 4 (HG/SS) Pokéathlon** ⚠️ **compile + test.** `Gen4ToolsWindow` covers the whole
  `Pokeathlon4` block (7 tabs) but has never been built or run — see gotcha #7. After it's green on a
  real HG/SS save, the obvious follow-ups are species sprites in the medal + participant editors,
  `FormConverter` name dropdowns in place of the numeric Form spinners, and the other HG/SS misc
  surfaces WinForms exposes in `SAV_Misc4` (Poké Walker, Battle Frontier, `Record4`, Underground).
- **Gen 6 (XY/ORAS)** ⚠️ **compile + test.** `Gen6ToolsWindow` covers the six XY+ORAS-shared
  features (Trainer, Poké Puffs, O-Powers, Super Training, Pokémon Link, Hall of Fame) but has never
  been built or run — see gotcha #7. After it's green on a real save, the obvious follow-ups are:
  ORAS-only tabs (Secret Base — the biggest single WinForms form in Gen 6 — and Pokéblocks/berry
  randomize), XY-only tabs (Berry Field viewer, Roamer, Friend Safari + accessory unlocks), the
  `FormConverter` name dropdowns to replace the numeric Form spinners, the 200-record trainer-stat
  grid, and the `TrainerFashion6` appearance surface (WinForms uses a `PropertyGrid`, which has no
  Avalonia equivalent — needs a real form or a generated one).
- **Performance** — legality runs on all 30 slots per box render; make it lazy/async if it lags.
- **File open/save** ✅ — `SavePanel` now opens and saves via the native picker
  (`StorageProvider.Open/SaveFilePickerAsync`, safe again on Avalonia 12.0.5; `SaveCurrentToDiskAsync`
  is the picker-based save used by the 💾 button and by both tool windows' persist callbacks).
- **CI** — build cross-platform projects + run Core tests on macOS/Linux runners; pixel-diff
  sprites against a Windows reference (the Phase 1 verification still owed).
- **The long tail** — port the per-generation save editor screens from `PKHeX.WinForms` as needed.

Keep `PKHeX.Core` pristine; all new code goes in `PKHeX.Avalonia` (and the ported drawing
projects). That keeps the fork mergeable against fast-moving upstream.
