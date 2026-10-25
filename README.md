# Siege AR

This is a LiDAR iPhone AR tower-defense game for HackTX 2026. You defend your castle, which sits on a real table, with a bow. An enemy knight works a catapult across the room and lobs fireballs at you.

Built with Unity 6 (6000.6.4f1), URP, AR Foundation 6.6 and ARKit.

## Run it on an iPhone

1. Build the Xcode project. It is already generated in `Build/iOS`. To regenerate it, run Unity's **Siege → Build iOS** menu, or this command:

   ```bash
   /Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath . -buildTarget iOS -executeMethod Siege.EditorTools.iOSBuild.Build -logFile -
   ```

2. Open `Build/iOS/Unity-iPhone.xcodeproj`.
3. On the **Unity-iPhone** target, open **Signing & Capabilities**, choose your Team, and set a unique bundle id if needed.
4. Plug in a LiDAR iPhone (12 Pro or newer Pro) and press Run.

You need landscape orientation and a well-lit room with a table.

## How to play

1. **Scan:** move the phone slowly until dots appear on the table or floor.
2. **Raise your castle:** aim the reticle at the table and tap.
3. **Place the enemy:** aim across the room, at least 0.7 m away, and tap. "PLACE IT FOR ME" drops the catapult 1.6 m away on whatever surface is there.
4. The castle builds itself, the catapult rolls in, and the war horn sounds.
5. **Shoot:** press anywhere and drag down to draw, aim with the phone (crosshair), and release to fire. A dotted arc previews the shot.
6. **Hit targets:** hits on the catapult splinter wood. Knight hits do double damage. The powder kegs beside the catapult explode for big damage.
7. **Win** by breaking the catapult. **Lose** when the keep (central tower with the crown) falls.

## Testing without a phone

In the Editor, press Play on `Assets/Siege/Scenes/Main.unity`. It runs a simulated room:

- **Left-drag:** draw and loose the bow.
- **Right-drag:** look around.
- **Scroll:** move the camera forward or back.

Headless play-through, which writes screenshots to `/tmp/claude-501/previews/sim_*.png`:

```bash
Unity -batchmode -projectPath . -executeMethod Siege.EditorTools.SimRunner.Run -siegeAutopilot victory -logFile /tmp/sim.log
```

Use `defeat` instead of `victory` to test the losing path, or `probe` for a quick smoke test.

## Regenerating content

Everything below `Assets/Siege` that isn't a script is generated:

| What | How |
|---|---|
| Catapult, knight, bow, arrow models | `Blender -b -P Tools/blender/build_models.py -- "$PWD"` |
| FX and UI textures | `python3 Tools/gen_textures.py "$PWD"` |
| All sound effects | `python3 Tools/gen_audio.py "$PWD"` |
| Materials, prefabs, scene, layers, project settings | Unity menu **Siege → Build All** (`Siege.EditorTools.SiegeAssetBuilder.BuildAll`) |

The models use UVs that point into the Kingdom pack's palette atlas. That keeps everything on one material, so it matches the pack's style and renders cheaply.

## Code map (`Assets/Siege/Scripts`)

- **Game**
  - `GameManager`: the flow (scan → place → intro → battle → win/lose).
  - `Castle` and `CastlePiece`: destructible sections, the castle-builds-itself intro and the collapse.
  - `Catapult`: crank, aim, fire, and damage / break-apart.
  - `Knight`: procedural animation and ragdoll.
  - `Fireball`, `TNTBarrel`, `LandingMarker`.
- **Player**
  - `BowController`: the on-screen bow, string, limb bend and aim arc.
  - `Arrow`: swept flight, sticking, and hit reactions.
  - `IBowInput` and `TouchBowInput`.
- **FX**
  - `VFX`: pooled particle effects built in code (explosions, fire, smoke, dust, splinters, fireworks).
  - `ScreenFX`: shake, vignette/chromatic aberration punch, heat flash, slow-mo.
  - `MeshFracture` and `Debris`.
- **Core**
  - `Haptics`: every Core Haptics pattern. The native side is `Assets/Plugins/iOS/SiegeHaptics.mm`.
  - `Sfx`: pooled 3D audio.
  - `Damage`: layers and the `Tune` gameplay numbers.
- **AR**
  - `ARSurface`: grid while placing, then an invisible shadow catcher with colliders on planes and the LiDAR mesh.
  - `ARLightMatch`.
  - `SimCameraRig`.

The world is game-scale. The XR Origin is scaled to 55 units per real metre, so physics behaves like a full-size castle. AR Foundation corrects LiDAR occlusion for that scale.

## Hooking up the IMU glove later

Implement `IBowInput` with these members:

- `Draw`: 0 to 1.
- `Drawing`
- `ReleasedThisFrame`
- `AimDirection`: optional world-space aim. Return `null` to keep aiming with the phone camera.

Then call `GameManager.Instance.bow.SetInput(yourGloveInput)`. Nothing else needs to change. Haptics, the aim arc, sounds and damage all read from the bow.

## Tuning

Gameplay numbers are in `Tune` (`Scripts/Core/Damage.cs`): catapult health, reload time, arrow speed, near-miss frequency and so on.

World scale and catapult distance are on the `Game` object (`GameManager`): `unitsPerMeter`, `catapultScale` and `autoCatapultDistanceM`.

## Assets

- The castle, scenery, kegs, coins, crown, flags and rubble come from **ithappy Kingdom (Unity 6)**.
- The catapult, knight, bow and arrow are custom procedural models.
- The Stylized Newbie Weapons Pack is linked to the Asset Store account but isn't downloaded into this project. To use its bow, import it through Package Manager → My Assets, then swap the meshes in `Prefabs/BowRig.prefab` (keep the `Limb_Top`, `Limb_Bottom` and tip transforms).
