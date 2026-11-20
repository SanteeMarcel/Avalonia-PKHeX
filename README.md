Avalonia PKHeX
==============

![License](https://img.shields.io/badge/License-GPLv3-blue.svg)
![Platforms](https://img.shields.io/badge/platforms-macOS%20%7C%20Linux%20%7C%20Windows-lightgrey.svg)
![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)

A cross-platform fork of [PKHeX](https://github.com/kwsch/PKHeX), the Pokémon core series save editor, that runs **natively on macOS and Linux**.

Upstream PKHeX is a Windows Forms app, so it only runs on Windows. This fork keeps PKHeX's save-parsing and legality engine (`PKHeX.Core`) **unchanged**, ports the sprite/drawing layer from `System.Drawing` to [SkiaSharp](https://github.com/mono/SkiaSharp), and replaces the WinForms UI with a new [Avalonia](https://avaloniaui.net/) front-end that is being built screen by screen.

> **Status: work in progress.** Box/party viewing, Pokémon editing and transfers work for every game PKHeX.Core supports. The per-game tool windows are at different stages: see the table below. Back up your saves before editing.

## Features

* **Open and save** any save file PKHeX.Core recognises (\*.sav, \*.dsv, \*.dat, \*.gci, \*.bin, …) using the native file picker.
* **Boxes and party**: sprites, box wallpapers, and a legal/illegal badge on every slot.
* **Pokémon editor** (double-click a slot): nickname, level, nature, ability, held item, moves with PP Ups, IVs and EVs (AVs on Let's Go), gender, form, ball, friendship, OT/trainer ID, met data and date, markings, ribbons, PID reroll, Make Shiny. It has a live legality report with a Re-check button.
* **Two-save transfer**: load two saves side by side and copy or move Pokémon between them. Cross-generation conversion is handled by PKHeX.Core, and backward transfers that would produce illegal Pokémon are refused.
* **Day Care / Nursery** viewer (read-only) for the games that have one, including nature (handy for Everstone breeding).
* **Per-game tools**: a tools button appears automatically for the games that have one:

| Games | Tools | Status |
|---|---|---|
| HeartGold / SoulSilver | Pokéathlon: points, Data Cards, Athlete Shop, medals, courses, best scores, counters, event records | ⚠️ Untested |
| Black / White, Black 2 / White 2 | Join Avenue (BW2), Memory Link, Global Link / Dream World, Entralink & Entrée Forest | ✅ Verified on a real save |
| X / Y, Omega Ruby / Alpha Sapphire | Trainer, Poké Puffs, O-Powers, Super Training, Pokémon Link, Hall of Fame | ⚠️ Untested |
| Let's Go, Pikachu! / Eevee! | Trainer info; Let's Go-aware boxes and party (packed storage, party-backed slots) | ⚠️ Untested |

"Untested" means the code is written but hasn't yet been built and checked on a real save, so expect rough edges.

Not ported yet: the remaining WinForms screens (other games' save editors, Mystery Gift database, Showdown/QR import, batch editor, etc.). Use upstream PKHeX on Windows for those.

PKHeX expects save files that are not encrypted with console-specific keys. Use a savedata manager to import and export savedata from the console ([Checkpoint](https://github.com/FlagBrew/Checkpoint), save_manager, [JKSM](https://github.com/J-D-K/JKSM), or SaveDataFiler).

**We do not support or condone cheating at the expense of others. Do not use significantly hacked Pokémon in battle or in trades with those who are unaware hacked Pokémon are in use.**

## Building and running

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
# Run the app
dotnet run --project PKHeX.Avalonia/PKHeX.Avalonia.csproj

# Core test suite
dotnet test Tests/PKHeX.Core.Tests/PKHeX.Core.Tests.csproj

# Headless sprite-pipeline check (no UI): writes PNGs for a save's boxes
dotnet run --project PKHeX.SpriteTest -- "/path/to/your.sav"
```

Build individual projects rather than the whole solution: `PKHeX.slnx` still includes the original `PKHeX.WinForms` project, which only builds on Windows.

## Project layout

| Project | What it is |
|---|---|
| `PKHeX.Core` | Upstream PKHeX's engine: save parsing, Pokémon data, legality, encryption. **Kept unmodified** so upstream updates merge cleanly. |
| `PKHeX.Drawing`, `PKHeX.Drawing.PokeSprite`, `PKHeX.Drawing.Misc` | Sprite and image code, ported from `System.Drawing` to SkiaSharp. |
| `PKHeX.Avalonia` | The new cross-platform UI. |
| `PKHeX.SpriteTest` | Console harness for checking the drawing layer without the UI. |
| `PKHeX.WinForms` | The original Windows UI, kept for reference and as the porting source. |
| `Porting/` | Migration notes (`SYSTEM_DRAWING_TO_SKIASHARP.md`, `PORTING_NOTES.md`). |

Contributor notes, including the porting gotchas and a PKHeX.Core API cheat-sheet, are in [`CLAUDE.md`](CLAUDE.md).

## Credits

All the hard work of save parsing and legality checking is [PKHeX](https://github.com/kwsch/PKHeX) by Kaphotics and its contributors. This fork only adds the cross-platform drawing layer and UI. Please report bugs in this port here, not upstream.

PKHeX's QR code generation code is taken from [QRCoder](https://github.com/codebude/QRCoder), which is licensed under [the MIT license](https://github.com/codebude/QRCoder/blob/master/LICENSE.txt).

PKHeX's shiny sprite collection is taken from [pokesprite](https://github.com/msikma/pokesprite), which is licensed under [the MIT license](https://github.com/msikma/pokesprite/blob/master/LICENSE).

PKHeX's Pokémon Legends: Arceus sprite collection is taken from the [National Pokédex - Icon Dex](https://www.deviantart.com/pikafan2000/art/National-Pokedex-Version-Delta-Icon-Dex-824897934) project and its abundance of collaborators and contributors.

The UI is built with [Avalonia](https://github.com/AvaloniaUI/Avalonia) (MIT), and drawing uses [SkiaSharp](https://github.com/mono/SkiaSharp) (MIT).

## License

GPLv3, same as upstream PKHeX. See [LICENSE](LICENSE).
