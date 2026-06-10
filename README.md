# Curse of the Night Rider

An NES-style **Super Scaler** open-world trading game built in Unity.

You are the **Headless Horseman**. Ride across an open world, trade your way up, and recover what was taken from you — your head.

## Premise

Cursed and headless, you roam the night on horseback. The world is open and the road is yours. Buy low, sell high, and barter your way through a chain of trades — each deal bringing you one step closer to getting your head back.

## Vibe

- **NES-era aesthetic** — pixel art, limited palette, chiptune energy.
- **Super Scaler presentation** — pseudo-3D sprite scaling (think *Space Harrier* / *Out Run*), but on a horse.
- **Open-world trading** — no fixed path; the economy is the game.
- **Trade-up loop** — start with scraps, end with a head.

## Status

🚗 Playable vertical slice — core systems built:

- **Navigation** — closed-loop lanes (Unity Splines), auto-discovered same-direction adjacency, snappy lane-jumping. Junctions are roundabouts; no forks or overpasses.
- **Super-scaler look** — speed-driven, view-correct scrolling road shader; animated self-illuminated sprites; black background.
- **Rider** — animated horseman sprite + a ghostly NES-dithered `<`/`>` attack apparition.
- **Traffic** — directional carriage sprites spawned ahead in your lane + neighbours; rear-end + attack; energy → wrecks → lupin pickups.
- **Economy** — lane-pinned trading-post ghosts; full-screen keyboard trade menu (build a basket, A/B, heads buy-once); Perlin `PriceMap` (per-good, per-location prices) with a Scene-view heatmap.
- **Input** — unified keyboard + joypad (`Controls`); Start = pause.

Still ahead: the **head-recovery progression** that ties it into a game (and head→music), then level building and the rest of the art.

## Tech

- **Engine:** Unity 6 (URP). Assets via Git LFS.
- **Input:** Unity Input System.
- Code lives in `Assets/Scripts/` (`World/` = sim, `View/` = presentation), shaders in `Assets/Shaders/`, art in `Assets/Art/`.

## Getting Started

1. Clone the repo.
2. Open the project in Unity (matching the version in `ProjectSettings/ProjectVersion.txt`).
3. Unity will regenerate the `Library/` folder on first open.
4. Open `Assets/Scenes/SampleScene.unity` to start.

## Credits

- **Dogica Pixel** font by Roberto Mocci — [SIL Open Font License 1.1](Assets/Art/Fonts/dogica_OFL_license.txt).
