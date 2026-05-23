# Enemy Cycle

I kept getting surprised by enemy attacks two or three turns out and
wanted to be able to just see what was coming. This shows the next 3
intents above each enemy's head and lets you click an enemy to see
their full move cycle in a modal.

## What it does

- Displays the next 3 intents above every enemy.
- Click any enemy to open a modal showing their full move pattern.
- Three preview modes — **Always show / Hover only / Never**. Setting
  is available in the main Settings screen and in the mod's info
  panel.
- ESC closes the cycle modal (instead of triggering the pause menu).
- The modal's hitbox disables itself when you're holding a card so it
  doesn't intercept clicks meant for targeting.
- No gameplay effect — pure visual.

## Known limits

- Some players consider intent previews a difficulty change. It's
  flagged as not affecting gameplay because the data is already shown
  via current-intent — this just exposes more turns of it.
- STS2 disables achievements while any mod is loaded — uninstall if
  you're chasing those.

## Install

### Steam Workshop

Subscribe via the game's Workshop page. Launch the game and enable the
mod from the in-game Mods screen.

### Manual

1. Download the zip from the [Releases page](../../releases).
2. Extract so the folder structure is
   `<game>/mods/EnemyCycle/{EnemyCycle.dll, mod_manifest.json}`.
   - Mac: `<game>/SlayTheSpire2.app/Contents/MacOS/mods/EnemyCycle/`
   - Windows/Linux: `<game>/mods/EnemyCycle/`
3. Launch the game and enable Enemy Cycle on the in-game Mods screen.

## Build from source

Requires .NET 9 SDK and a local copy of Slay the Spire 2.

```
./build.sh
```

The build script compiles `EnemyCycle.dll` and copies it + the
manifest into your game's `mods/` folder.

## Companion mods

- [Retry](https://github.com/sts2mods/Retry) — replay any past run
  from any floor.
- [Run Table](https://github.com/sts2mods/RunTable) — searchable
  table of your past runs.
- [Timeline](https://github.com/sts2mods/Timeline) — in-combat
  timeline of every event.

## License

MIT.
