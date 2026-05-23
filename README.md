# Enemy Cycle

This mod just lets you optionally see the enemies next 2 moves above their head(unless the move is a random move, then it doesn't show up, you don't get any extra information I tried to make this just like having the wiki open next to you), and clicking above the enemy opens up a modal showing off all of their moves as well as how their cycle works in the patterns section.  I also just threw in the beastiary stuff so you can see the enemy's animations when they use a specific move.  This is all programmatically generated based on the enemies so unless there is a completely bespoke interaction(or just an interaction type I didn't cover) then it should work as they ship balance updates that change things.

bee guy with his moves above his head

<img width="427" height="476" alt="Screenshot 2026-05-23 at 12 38 20 PM" src="https://github.com/user-attachments/assets/36b5e121-4617-47dd-8f72-f4dfc95f7def" />

modal that shows his cycle

<img width="979" height="587" alt="Screenshot 2026-05-23 at 12 38 46 PM" src="https://github.com/user-attachments/assets/e92d7f14-6e81-4028-b92e-2e8d9422f684" />

a couple more examples of enemies with varying cycles:

<img width="801" height="538" alt="Screenshot 2026-05-23 at 12 40 24 PM" src="https://github.com/user-attachments/assets/8393aaca-9492-4b3b-a175-c138ca9034b3" />
<img width="812" height="542" alt="Screenshot 2026-05-23 at 12 42 33 PM" src="https://github.com/user-attachments/assets/c5c37b05-ba1c-4152-a281-0724ecb99e01" />
<img width="812" height="558" alt="Screenshot 2026-05-23 at 12 41 28 PM" src="https://github.com/user-attachments/assets/3abe69cf-cce0-46f3-af15-e41088f4d250" />


I made 3 different modes as well for the preview above enemies, it can either be always on, always off (so you have to just click to see the popup to get any info), or shows on hover.

If you have any suggestions or find bugs, leave them as issues here and I can fix them.

The rest of this was made by claude so it's probably right but who knows:

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
