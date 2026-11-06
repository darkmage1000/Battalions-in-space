# Battalions in Space — large-scale team shooter (Unity 6)

Unity 6000.0.62f1 LTS · Built-in render pipeline · legacy Input Manager (+ gamepad axes) · Windows target (Android builds too) · MCP for Unity bridge.
All IP is original: four factions, their soldiers, heroes, ships, planets and the galaxy. Mechanics follow the 2005-era team shooters (command posts, tickets, heroes earned by points, class awards, space battles with boarding, a galactic campaign). Never use names, characters, designs or sounds from any existing franchise.

## Run
Open `Assets/Scenes/Main.unity` (empty) and press Play: `Game` boots itself (`[RuntimeInitializeOnLoadMethod]`) and builds everything in code — camera, sun, menus, battles. Nothing is authored in the scene and there are no art assets: models come from `MeshKit`, sounds/music from `Sfx`.
Menu **Battalions > Setup Project** (also runs on load): scene + build settings, player settings, gamepad axes. **Battalions > Build Windows** → `Builds/Windows/BattalionsInSpace.exe`; **Build Android** → `Builds/Android/BattalionsInSpace.apk`.
Build self-test: `BattalionsInSpace.exe -autotest -screen-fullscreen 0` plays every map 12 s with the player spawned, opens a campaign, writes `%USERPROFILE%/AppData/LocalLow/Battalions Studio/Battalions in Space/autotest.txt` and quits.

## Testing in the editor (MCP)
The editor does not tick Play mode while it is in the background. Use `BIS.EditorTools.BattleTest` from `execute_code`: `Start(space, map, f0, f1)`, `Step(seconds)` (frames of 0.02 s via `EditorApplication.Step`, ~60 s of game per call max or the MCP call times out), `SpawnPlayer(cls, ship)`, `Place(pos, look)`, `Report()`. The first call right after entering Play mode often does nothing — make a warm-up call. Screenshots: `manage_camera screenshot` with `output_folder: "Previews"` (git-ignored). Stop Play mode before recompiling.

## Architecture (Assets/Scripts, namespace BIS)
- **Core/** `Game` (bootstrap, menus backdrop, `StartBattle`), `Battle` (one battle: teams, `Participant`s, spawning, tickets + bleed, points, hero unlock, class awards, win rules, results; `Bonus` table for Conquest), `Ctl` (all input: keyboard/mouse + Xbox pad via `PadLX..PadDY` axes) + `Settings` (PlayerPrefs), `Mats` (materials from the three shaders in `Resources/Shaders`), `MeshKit` (flat-shaded vertex-colour shapes; colour alpha 0 = glows), `Sfx` (synthesised SFX + march music, pooled 3D sources), `Fx` (two particle systems fed with Emit), `AutoTest`.
- **Data/** `Defs` (WeaponDef / ClassDef / HeroDef / VehicleDef / FactionDef), `Factions` (the four factions + weapon templates `W`), `Maps` (4 land maps with post layouts, 3 space maps, themes).
- **World/** `LandWorld` (height-field terrain in 64 mesh-collider chunks, themed props with primitive colliders, outposts, posts + vehicle pads, sky/fog, minimap texture), `CommandPost` (neutralise → capture, vehicle pads and respawn).
- **Units/** `Unit` (soldier/hero: CharacterController movement, heat/ammo weapons, equipment, specials, hero abilities, death), `SoldierRig` (part model per body style + procedural animation), `Combat` (layers, `IDamageable`, `Participant`, `Shots` = every projectile drawn with `Graphics.DrawMesh`), `Gear` (mines, detpacks, supply crates, pickups), `PlayerControl` (input → unit/vehicle, cameras, spawn/dead/pause flow).
- **Vehicles/** `Vehicle` (ground hover vehicles, gunships, starfighters; seats: driver / gunner / passengers; lock-on, repair pulse, landing), `VehicleModels` (per-faction code-built models).
- **Space/** `CapitalShip` (box-built hull with a real hangar + interior corridors, `Subsystem`s: shield, 2 engines, 7 turrets, hangar control, life support, reactor; hull falls mostly through systems), `SpaceWorld` (two ships 1300 m apart, sky, planet / nebula / asteroids).
- **AI/** `BotBrain` (land infantry, drivers, gunners, gunship pilots, space jobs Dogfight / Bomb / Board / Escort / Defend / Sabotage, ship interior nav graph).
- **Conquest/** `Campaign` (galaxy of 12 worlds + hyperlanes, owners, fleets, credits, bonuses, class unlocks, AI turns, auto-resolve, JSON save `conquest.json`), `GalaxyView` (3D map).
- **UI/** IMGUI in a virtual 1080-high space: `UI` (helpers), `HUD` (battle HUD, radar, spawn screen, map, scoreboard, pause, results), `MenuUI` (+ partial `ConquestUI`).

## Rules of thumb
- Layers by number only: 8 Ground, 9 Unit, 10 Vehicle, 11 Hull (`Layers`). Friendly fire is off; damage goes through `Shots.Deal` (applies `vsVehicle`, bonuses, hit markers).
- Capital ship local frame: bow +x, hangar opens on +z toward the enemy, hangar floor at y = 0. Interior nav nodes in `CapitalShip.nodes` / `links`.
- Bots set one-shot inputs (swap/equip/ability/jump) and `Unit.Tick` clears them.
- New content = add to `Factions` / `Maps` / `Galaxy.Planets`; models follow `FactionDef.body`.

## Status
- **2026-11-05 — first full build.** 4 factions (Terran Concord, Vorrkai Dominion, Iron Synod, Kessari Veil), each 5 classes + pilot/marine, 4 heroes (Blade / Gunslinger / Juggernaut / Mystic archetypes), tank, transport, gunship, 4 starships, a capital ship. 4 land maps (Harrak Dunes, Elos Highwood, Frostfall Ridge, Cinder Reach), 3 space maps (Harrak Orbit, Veil Nebula, Shattered Belt). Quickplay + Galactic Conquest. Windows build passes `-autotest`.
- Checked in the editor: land battles (captures, bleeding, vehicles used by bots), hero change (H), space battles (bombers kill shields/engines, ~7 min to a ticket win), bot boarding → reactor sabotage with detpacks, player landing in the enemy hangar and getting out, conquest invasion → battle → capture, a 60-turn auto-resolved campaign to a winner.
- Touch controls (`Core/TouchInput.cs`): floating move stick left, drag-look right, buttons; on automatically on phones, Options > "Touch controls" forces them on a PC touch screen. Menus are IMGUI, so taps work as clicks. Android: IL2CPP ARM64, `Battalions > Build Android`.
- Q in a vehicle switches seat (driver / gunner / passenger). Abandoned starships self-destruct after 90 s (they have no pads).
- Not yet checked by hand: how flying feels with a mouse, gamepad on real hardware, the APK on a phone.
