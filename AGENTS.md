# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

Four games on a glowing grid in the style of Bally Midway's 1982 arcade game Tron, in C# on .NET 10 and
Avalonia 12, drawn through Avalonia's `DrawingContext`: light cycles, a tank maze, the grid bugs' gate and the core. One
self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). Everything in it is original: the
graphics are drawn in code, and the sounds and tunes are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVALightCycle.slnx                   # the SDK version is in global.json (10.0.400 or later)
dotnet test AVALightCycle.slnx                    # every test runs headless: no window opens
dotnet run --project src/AVALightCycle            # play it; --no-audio for silence
GRID_SHOTS=$PWD/Images dotnet test tests/AVALightCycle.Tests --filter ScreenshotTests   # refresh the README's pictures
GRID_PROBE=1 dotnet test tests/AVALightCycle.Tests --filter GridProbe   # how the computer player does (for tuning)
python3 tools/make_sounds.py                      # rebuild src/AVALightCycle/Sounds (standard-library Python only)
```

`GridProbe` does nothing unless `GRID_PROBE` is set (to anything). It plays each game at several levels and
prints how often the computer player wins.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVALightCycle/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and a level's phases, including
    the screen where the player picks the next game.
  - `GameEngine.Play.cs` starts games and levels, and tracks which of the four are won.
  - `Entities.cs` has `Vec`, the `Controls` record, the `Field` measures and fixed step, and the abstract
    `Stage` that each game derives from.
  - `CycleStage.cs`, `TankStage.cs`, `BugStage.cs` and `CoreStage.cs` are the four games.
  - `GridPilot.cs` is the computer player, with a way of playing each game; used by the attract mode and tests.
  - `InputHandler.cs` (keys and mouse) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVALightCycle/Views/`: `GameCanvas` (split over partial files) draws the frame and runs the 60 Hz timer.
- `src/AVALightCycle/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVALightCycle.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: `GameplayTests`, `ScreenshotTests` and
  `GridProbe`.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` advances in fixed 1/60 s steps (`Field.Step`), independent of the frame
  rate, so tests can play whole games deterministically from a seed. Draw random numbers from the engine's
  seeded `Random` (`G.Rng` inside a stage), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it. A new game is a new `Stage` subclass and a `StageKind`.
- **Input goes through `InputHandler`.** Up/down on keys and stick must produce the same `Dy` sign the engine
  expects (negative is up the screen). Test new controls through `InputHandler`, not by setting `Controls`
  directly.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVALightCycle`); tests
  load the table from a temporary folder (`TempFolder()`) and run silent (`Silent = true`).
- **Line endings.** Keep each file's existing line endings (`app.manifest` is CRLF) so diffs show only real
  changes.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list, the project structure) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
