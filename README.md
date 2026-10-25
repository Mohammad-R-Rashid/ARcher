# ARcher: Siege AR

**Hold the castle. In your living room. With a real bow glove.**

ARcher is an augmented-reality tower-defense game for iPhone, built at **HackTX 2026** for the medieval theme. Point a
LiDAR iPhone at your floor and a stone castle rises around you, with a whole town spread across the room. You stand on the
keep like the archer on a Clash Royale princess tower. Across the room, an enemy army sets up camp: trebuchets lob
flaming boulders at your walls, a battering ram pounds the gate, and soldiers carry ladders up to the battlements.

You fight back with a bow. Draw on the screen, or wear the **bow glove**: clench your fist to draw and open your hand
to loose.

---

## The game

| | |
|---|---|
| **Your castle** | A full diorama on your real floor: keep, towers, walls, gatehouse, and a town of cottages, market stalls and watchtowers. It's sized to your height, so the battlements sit just below your phone. |
| **The siege** | Up to three counterweight trebuchets, a siege camp, a knight giving orders, and one escalating raid: dozens of footmen, ladder teams and a ram crew. |
| **You** | The archer on the keep. Aim with the phone's crosshair, draw, and release. A light pull drops short and a full draw flies true. You can also shoot incoming fireballs out of the air. |
| **Your allies** | Archers in blue on the towers and gate. They help a little, but you're the main defender. |
| **Win** | Break the raid. In demo mode the gate and keep take a beating but never fall, so a demo always ends in victory. |

### What makes it feel real
- **LiDAR-aware battlefield.** The scanned room becomes a walkability map. Furniture, walls and anything overhead are
  obstacles, and a flood fill from your gate marks which floor is actually reachable. Trebuchets and the camp only set
  up on open floor, soldiers spawn in front of you and never inside a wall, and raiders path around your couch.
- **Real occlusion and lighting.** People and furniture hide the castle. The sun matches your room's brightness and
  colour, and the castle casts shadows onto your real floor.
- **Physics you can feel.**
  - Each trebuchet's arm swings because its counterweight drops.
  - The sling whips round and lets go at the right moment, so the fireball flies a true ballistic arc.
  - Arrows fly fast and flat, stick where they land, and shatter powder kegs.
  - Castle sections crumble into rubble.
- **Haptics on everything** (Core Haptics):
  - bowstring tension as you draw
  - a thump when you release
  - a heartbeat as a fireball closes in
  - a rumble when a tower collapses
- **Sound with the ringer off.** Every sound effect is synthesized.

---

## The bow glove

A glove with a flex sensor and an IMU, built on a Seeed XIAO ESP32-S3. It joins the phone's Personal Hotspot and sends
draw, release and heartbeat messages as JSON over **UDP port 9000**.

- **Draw:** clench your fist. Draw power comes from how far your fingers bend.
- **Fire:** open your hand.
- **Touch still works:** whichever starts a draw (glove or touch) owns that shot.
- **Status:** a line at the top-right shows `Glove: connected · IMU ok`.

The firmware, flashing tool and test page are in [`glove/`](glove/README.md). How the app receives the messages is
in [`GLOVE_INTEGRATION.md`](GLOVE_INTEGRATION.md) and [`glove/HANDOFF.md`](glove/HANDOFF.md).

---

## How to play

1. Turn on **Personal Hotspot** (Maximize Compatibility) if you're using the glove, and allow **Local Network**
   access when the app asks.
2. Hold the phone in landscape and scan the floor until the dots appear.
3. Stand where you want your tower and tap. The keep rises under your feet, and the castle and town build outward.
4. The trebuchets roll in and the war horn sounds.
5. **Hold to draw, then release to fire.** Aim at soldiers carrying ladders, the ram crew, incoming fireballs, the
   knight, or the powder kegs beside the trebuchets.

---

## Tech

- **Unity 6** (6000.6.4f1), URP, **AR Foundation 6.6 / ARKit**: world tracking, LiDAR scene mesh, environment depth
  occlusion, light estimation, environment probes.
- **Procedural content:**
  - Blender Python scripts model the trebuchet, army, knight, town and arrow on the Kingdom pack's palette atlas.
  - Python generates the textures and synthesizes every sound.
  - One editor builder assembles all the prefabs and the scene.
- **Native iOS plugin:** Core Haptics patterns, plus an audio session that ignores the ringer switch.
- **Glove:** Arduino firmware on an ESP32-S3, and a C# UDP link with packet-loss handling (`Assets/Siege/Scripts/Glove`).
- **Assets:** ithappy *Kingdom* (castle, scenery) and *Stylized Newbie Weapons Pack* (bow). Everything else is generated.

### Build and run

1. Regenerate everything from the Unity menu **Siege → Build All**, or headless:

   ```bash
   /Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath . -buildTarget iOS -executeMethod Siege.EditorTools.SiegeAssetBuilder.BuildAll -logFile -
   ```

2. Build the Xcode project:

   ```bash
   /Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath . -buildTarget iOS -executeMethod Siege.EditorTools.iOSBuild.Build -logFile -
   ```

3. Open `Build/iOS/Unity-iPhone.xcodeproj`, pick your team, and run on a LiDAR iPhone (12 Pro or newer Pro).

**Playtest without a phone:** run `Siege.EditorTools.SimRunner.Run` with `-siegeAutopilot victory|defeat|obstacles`.
It plays a full round in a simulated room and saves screenshots.

**Generators:**
- Blender: `Tools/blender/build_*.py`
- Textures: `Tools/gen_textures.py`
- Audio: `Tools/gen_audio.py`

**Tuning:** gameplay numbers are in `Assets/Siege/Scripts/Core/Damage.cs` (`Tune`). Set `DemoNoLoss = false` to allow
real defeats.

### Code map (`Assets/Siege/Scripts`)
- **Game:**
  - `GameManager`: game flow and LiDAR placement.
  - `NavGrid`: room walkability and pathfinding.
  - `Castle` and `CastlePiece`.
  - `Trebuchet`.
  - `WaveManager`, `Soldier`, `SiegeLadder`, `BatteringRam`.
  - `Defender`, `Knight`, `Fireball`.
- **Player:**
  - `BowController`: aiming, aim assist and firing.
  - `BowBender`: real limb bend.
  - `Arrow`.
  - `TouchBowInput`.
- **Glove:** `GloveLink`, `GloveUdp`, `GloveBowInput`, `CombinedBowInput`, `GloveSetup`, `GloveStatusUI`.
- **FX and Core:** `VFX`, `ScreenFX`, `MeshFracture`, `Haptics`, `Sfx`.

---

## Team

Built in a weekend at HackTX 2026 by **Mohammad Rashid**, **Bilal Shihab** and **Jay Damodaran**.
