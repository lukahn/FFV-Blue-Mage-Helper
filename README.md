# FFV Blue Mage Helper

> **AI disclosure:** This mod — source code, documentation and debugging — was produced with the help
> of an AI assistant (GitHub Copilot in VS Code). It has been tested in-game, but it comes with no
> warranty: review the source and back up your saves before relying on it.

A BepInEx plugin for **FINAL FANTASY V Pixel Remaster** that watches the enemies
in each battle and shows a Final Fantasy-style blue message box when an enemy can
teach a Blue Magic spell your party has not learned yet.

The banner lists the enemy and the spell, for example:

```
BLUE MAGIC
Black Goblin — Goblin Punch
```

It only appears when there is something new to learn, and it fades away on its
own.

## Requirements

- FINAL FANTASY V Pixel Remaster (Steam)
- BepInEx 6 IL2CPP (`6.0.0-be.697` at the time of writing) set up per
  [the usual Pixel Remaster modding guide](https://steamcommunity.com/sharedfiles/filedetails/?id=2788533275)
  - Magicite is not required by this plugin, but it works fine alongside it
  - On a Steam Deck or other Linux/Proton setup, see
    [Steam Deck](#steam-deck-and-other-linuxproton-setups) below — BepInEx needs one extra step
    there
- The custom `UnityPlayer.dll` required by the Pixel Remaster modding scene
- The interop assemblies BepInEx generates on first launch (`BepInEx/interop`)

## Build and install

```powershell
dotnet build -c Release -p:DeployToPlugins=true
```

That copies `FFVBlueMageHelper.dll` into `BepInEx/plugins`. Without
`-p:DeployToPlugins=true` the build output lands in `bin/Release/net6.0/` and can
be copied manually.

If your game is not at the default Steam path, override it:

```powershell
dotnet build -c Release -p:GameDir="D:\Games\FINAL FANTASY V PR" -p:DeployToPlugins=true
```

The project references the interop assemblies from `$(GameDir)\BepInEx\interop`,
so BepInEx must have run at least once before the plugin can be compiled.

## Steam Deck (and other Linux/Proton setups)

The Steam build is Windows-only, so on a Steam Deck the game runs through Proton.
BepInEx will not start unless Proton is told to load the game's own
`winhttp.dll`, which is Doorstop's injection point. Without that override the
game runs normally but BepInEx never initialises: no `BepInEx/config`, no
`BepInEx/interop`, no `LogOutput.log` — the `BepInEx` folder just keeps the
stock `core`, `patchers` and `plugins` directories.

1. Install the **Windows x64** BepInEx 6 IL2CPP pack into
   `.../steamapps/common/FINAL FANTASY V PR`, copying the whole archive.
   `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and the `dotnet`
   folder belong next to the game executable, alongside `BepInEx/`; copying only
   the `BepInEx` folder is not enough.
   * `.doorstop_version` starts with a dot, so Dolphin hides it unless hidden
     files are enabled.
   * The folder must be named `BepInEx` exactly — ext4 on the SD card is
     case-sensitive.
2. In Steam, open FINAL FANTASY V → **Properties → General → Launch Options** and
   enter:

   ```
   WINEDLLOVERRIDES="winhttp=n,b" %command%
   ```

   This is the step that makes BepInEx load under Proton. If you'd rather not use
   launch options, the equivalent one-off setup is protontricks → the game →
   `Select the default wineprefix` → `Run winecfg` → **Libraries** → add
   `winhttp` → Apply.
3. Launch the game **from Steam** and leave it at the title screen while BepInEx
   generates `BepInEx/interop`; the first launch is slow. `BepInEx/LogOutput.log`
   should appear afterwards.
4. Copy `FFVBlueMageHelper.dll` into `BepInEx/plugins` and confirm the log
   contains `FFV Blue Mage Helper ... loaded`.

Build against the same BepInEx build that is installed on the Deck; if the Deck
has a different BepInEx 6 build, rebuild with that version so the interop
assemblies match.

## Configuration

The config file is created at
`BepInEx\config\com.lukahn.ffv.bluemagehelper.cfg`.

| Setting | Default | Description |
| --- | --- | --- |
| `Display.ShowDurationSeconds` | `8` | How long the banner stays on screen. |
| `Display.MaxLines` | `6` | Maximum enemy/spell lines. Extra lines become `+N more`. |
| `Debug.VerboseLogging` | `false` | Logs detected enemies, learned blue magic IDs and the game's blue magic mapping. |
| `Debug.ShowTestBannerOnStartup` | `false` | Shows a sample banner at startup. Also saves `BlueMageBannerTest.png` in the game folder so you can check the layout. |

## How it works

- Harmony hooks `BattleController.StartInBattle`.
- The monster party for the battle is read from the game's master data.
- Each monster ID is matched against the Blue Magic guide data embedded in
  `data/blue_magic.json`.
- Learned spells are read from the party's `LearningAbilitys`, owned abilities and
  the global `learnedAbilityList`. The game stores learned blue magic as enemy
  ability aliases (for example `400` for Goblin Punch), so the embedded alias
  table maps those back to the blue spell IDs (`205`-`234`). A spell counts as
  learned if any recruited character knows it.
- Spells learned during play are remembered for the rest of the session, and the
  learned state is re-read when a battle ends. Loading a save or starting a new
  game clears that session cache, so an older save is never suppressed by a spell
  learned in a different save.
- If at least one listed spell is still unknown, the banner is built on a small
  screen-space overlay canvas.

The blue box is drawn by the plugin itself (generated gradient/border texture),
so it does not depend on the game's window prefabs.

## Data

`data/blue_magic.json` was generated from the community guide:

<https://steamcommunity.com/sharedfiles/filedetails/?id=2611986585>

The guide's enemy names were matched to the Pixel Remaster monster IDs. A few
guide names need special handling:

- **Manticore** is the Pixel Remaster's **Chimera Brain**.
- **Crystal (#279 / #280)** refers to the **Water Crystal** and **Wind Crystal**
  bosses, which are nameless (`????`) in battle.
- **Behemoth**, **Dark Elemental** and **Soul Eater** are Advance/2013-only
  Sealed Temple enemies and do not exist in the Pixel Remaster, so they are
  omitted.

Spells that require the enemy to be controlled (such as `White Wind` from an
Enchanted Fan) are still shown, because the enemy does know the spell.

The file is embedded into the DLL at build time. To change the data, edit the
JSON and rebuild:

```jsonc
{
  "spells": [
    {
      "id": 222,                    // ability/magic id, 205-234 for blue magic
      "name": "Goblin Punch",       // English fallback name
      "enemies": [1, 8, 252, 367]   // Pixel Remaster monster ids
    }
  ],
  "monsters": [
    { "id": 1, "name": "Goblin", "msg": "MSG_MON_NAME_94" }
  ]
}
```

## Troubleshooting

- Set `Debug.VerboseLogging = true` and check `BepInEx\LogOutput.log`.
  The `[SelfTest]` lines confirm that master data, messages and the blue magic
  provider are available.
- Set `Debug.ShowTestBannerOnStartup = true` to check that the blue box renders.
  A screenshot is written to `BlueMageBannerTest.png` in the game folder.
- If no banner appears in battle, check that the battle actually contains one of
  the monsters listed in `data/blue_magic.json`, and that every spell it teaches
  has not already been learned.

## Known limitations

- The plugin is read-only. It does not change saves, battles or blue magic
  learning itself.
- Azulmagia can use a large number of blue spells; the banner is capped by
  `MaxLines`.
- Enemy display names use the game's own localized text when available, with the
  embedded English name as a fallback.

## Uninstall

Delete `BepInEx/plugins/FFVBlueMageHelper.dll` (and optionally
`BepInEx/config/com.lukahn.ffv.bluemagehelper.cfg`).
