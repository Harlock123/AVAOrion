# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A turn-based space strategy game in the style of MicroProse's 1993 Master of Orion, in C# on .NET 10 and
Avalonia 12. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The peoples,
worlds, words and pictures are original; the graphics are drawn in code, and the sounds and music are synthesised
by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAOrion.slnx                  # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAOrion.slnx                   # every test runs headless: no window opens
dotnet run --project src/AVAOrion           # play it
ORION_SHOTS=$PWD/Images dotnet test tests/AVAOrion.Tests --filter ScreenshotTests   # refresh the README's pictures
ORION_BALANCE=1 ORION_GAMES=3 dotnet test tests/AVAOrion.Tests --filter BalanceProbe --logger "console;verbosity=detailed"
python3 tools/make_sounds.py                # rebuild src/AVAOrion/Sounds (standard-library Python only)
```

`BalanceProbe` does nothing unless `ORION_BALANCE` is set; `ORION_FIRST` and `ORION_GAMES` pick the first seed and
the number of computer-only games. `Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference
types, implicit usings and invariant globalization for every project: a warning fails the build.

## Layout

- `src/AVAOrion/Rules/`: the fixed tables `Planets`, `Races`, `Ships`, `Techs`, the `Sfx` list, and
  `Rng` (with `Dice`).
- `src/AVAOrion/World/`: the galaxy and its rules, with no UI.
  - `WorldState.cs` is a game in progress, saved as JSON; `Entities.cs` has stars, colonies, fleets and empires.
  - `GalaxyMaker.cs` builds a galaxy from an `Rng`; `Designer.cs` draws up ship designs.
  - `Sim.cs` and its partial files (`Colonies`, `Fleets`, `Combat`, `Empires`) apply the rules for a year.
  - `Ai.cs` gives the computer rulers' orders; `SaveStore.cs` keeps five save slots.
- `src/AVAOrion/Game/`: `GameEngine` (split over partial files) runs the title, turns, orders and screens as a
  stack of `Panel`s; `SoundManager.cs` plays `Sfx` values.
- `src/AVAOrion/Views/`: `GameCanvas` (split over partial files) draws the galaxy, colonies, battles and the
  design room, and runs a 16 ms `DispatcherTimer`.
- `src/AVAOrion/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAOrion.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `Helpers.cs` starts games and plays years;
  `Sandbox` builds a tiny two-people galaxy for testing one rule.

## Rules that keep it working

- **Turns, not frames.** The game moves on only when a year ends (`EndTurn`). `GameEngine.Update(dt)` only drives
  the title, the attract demo and battle sounds, so game rules belong in `Sim`, not in the timer.
- **One source of chance.** All rules draw from the `Sim`'s seeded `Rng`, never `Random`. Saving writes
  `Rng.State` into `WorldState.Seed`, and loading starts a new `Rng` from it, so a loaded game carries on exactly.
  Any new random state must go through it too.
- **Keys arrive by name.** `GameCanvas.KeyName` turns Avalonia keys into names ("Up", "Enter", "F") and
  `GameEngine.Press` takes them. Tests press names too, so a new control needs a name and an engine handler,
  not code in the view. While a panel has an `Entry`, letters and digits come in as typed text instead.
- **Panels rebuild themselves.** A panel made with `Show(make)` is rebuilt by `Refresh()` after a choice, so
  build its text and options inside `make`, not once.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up. Each `Sfx` value plays the WAV of the same name in
  `Sounds/`.
- **Tests never touch the player's files.** Saves live in the app-data folder (`AVAOrion`); tests use
  `Helpers.TempSaves()`, a fresh temporary folder, and a silent `SoundManager`.
- **Line endings.** Sources are LF; `app.manifest` is CRLF. Keep each file's existing endings.

## Making a change

- Add or update tests with every rule change. `dotnet test` must pass, with no warnings.
- After a balance change, run `BalanceProbe` for a few games and read the outcome.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py` and the `Sfx` enum, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64, kept as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
