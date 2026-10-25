# Siege AR — "Raid" upgrade contracts

Shared spec for the parallel work streams. Every stream builds against these names/APIs; integration
(SiegeAssetBuilder, GameManager, HUD, AutoPilot) happens afterwards in one place.

Project: `SiegeAR/` (Unity 6000.6.4f1, URP, AR Foundation 6.6, namespace `Siege`). Read `README.md` first.
Do NOT run Unity (the project is locked by the integrator; parallel Unity instances corrupt the Library).
Blender (`/Applications/Blender 4.2.1.app/Contents/MacOS/Blender`) can be run freely.

## World conventions
- **1 Unity unit = 1 metre in-game.** The Kingdom keep's battlements are 12.6 m high, walls ~5.5 m,
  towers ~7.9 m. The XR origin is scaled (`GameManager.unitsPerMeter`, ~11–14 real-units/m) so the castle
  diorama is ~2.5 m across in the room. Never hard-code room metres; think in game metres.
- Gravity becomes `Tune.Gravity = 13` (slightly heavier than Earth for game feel).
- Layers (`Layers` in `Scripts/Core/Damage.cs`): Castle 8, Enemy 9, Projectile 10, Debris 11, Environment 12.
  Ground for walking = raycast down on `Layers.Mask(Layers.Environment)` (real floor planes/LiDAR mesh,
  the castle island, the sim floor). Soldiers/trebuchet/crew/knight/ladders/ram are on `Enemy`.
- Existing APIs you may call: `VFX.*`, `Sfx.Play/Loop/Play2D` (only the existing `Sound` enum values),
  `Haptics.*` (existing methods only), `ScreenFX.Shake/Punch/Heat/Damage/SlowMo`, `MeshFracture.Shatter`,
  `Debris` component, `Ease.*`, `MathExt.Damp`, `Castle` (`pieces`, `keep`, `PickTarget()`,
  `AimPoint(piece, from)`, `Blast(...)`, `Center`, `Fallen`), `CastlePiece` (`kind`, `Alive`, `Health`,
  `maxHealth`, `WorldBounds`, `TakeHit(HitInfo)`; the gate is the piece with `kind == PieceKind.Gate`),
  `Fireball` (`Launch(...)`, `Ignite()`, `Active`, `NearMiss`, `TimeToImpact`), `Knight`, `TNTBarrel`,
  `LandingMarker`, `GameManager.Instance` (`unitsPerMeter`, `cam`, `State`, `Castle`).

## Model export conventions (Blender)
- Use `Tools/blender/siege_common.py` (`from siege_common import *` after
  `sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))`). It gives the palette `C` / `swatch()`
  (every part's UVs point at one swatch of the ithappy palette atlas
  `Assets/ithappy/Location_Kingdom/Textures/Textures.png`, 19 cols × 10 rows of 51.2 px cells in the top half),
  primitives `box/cyl/sphere/dome/tube/torus/beam/sweep/rivets`, `join(objs, name, pivot)`, `empty(name, loc)`,
  `export(path)`. You may add new palette entries locally in your script (`C["x"] = swatch(col,row)`).
- Blender +Y = model front (becomes Unity +Z), +Z = up. Units = metres. `export()` flattens hierarchy and
  rotates for Unity; Unity rebuilds hierarchy **by object name**, so names below are exact.
- Style: the ithappy "rounded toy" look already used by `build_models.py` (generous bevels, smooth-by-angle,
  rope lashings, iron bands/rivets, slightly chunky proportions) but **life-size proportions**.
- Verify visually: render previews **in Blender** (EEVEE or Workbench) with a temporary material that uses the
  atlas image texture so colours show; save PNGs to `/tmp/claude-501/previews/` and look at them. Iterate until it
  looks good from the game's viewpoint (low 3/4 views from 30–60 m away and close-ups).

## Stream A — Trebuchet model  (`Tools/blender/build_trebuchet.py` → `Assets/Siege/Models/Trebuchet.fbx`)
Counterweight trebuchet, ~18–21 m tall with the arm vertical. Objects:
| name | pivot (Blender coords) | notes |
|---|---|---|
| `Frame` | (0,0,0) ground centre | twin base sills along Y (y −6..+6) at x≈±2.3, cross ties, two A-frame towers meeting at the axle bearings, diagonal braces, ground trough along the back (y −13..−3), windlass posts, iron straps/rivets, rope lashings. Axle at (0,0,9.0). |
| `Arm` | (0,0,9.0) axle | modelled **horizontal**: long arm to the back ending at y=−12.5 (tapering, iron bands, rope wraps, sling hook at tip); short arm to the front ending at y=+3.2 with a hinge pin. |
| `Counterweight` | (0,3.2,9.0) hinge | box of planks + iron corners + stones heaped on top, ~2.6×2.4×2.6 m, top ~1.0 m **below** the pivot, hung by two iron straps from the pivot. |
| `Windlass` | (0,−5.0,1.3) its axis | drum along X between the windlass posts with rope wound on it; capstan bars/spoked handles at both ends. |
| `Pouch` | its centre, placed at (0,−12.5,0.6) | leather sling pouch (~0.9 m), open upward. |
| empties | | `SlingTip` (0,−12.5,9.0); `RopeArm` (0,−10.5,8.7); `RopeWinch` (0,−5.0,1.6); `KnightSpot` (3.4,−3.0,0); `CrewSpot1` (−2.7,−5.0,0), `CrewSpot2` (2.7,−5.0,0), `CrewSpot3` (−1.6,−7.0,0), `CrewSpot4` (1.6,−7.0,0); `AmmoSpot` (−4.0,−1.0,0); `BannerSpot` (2.4,−6.0,0.6); `CampSpot` (0,−18,0) |
Budget ≤ 50k tris total. Wood/iron/rope palette like the catapult (`wood`, `wood_dark`, `iron`, `rope`, `leather`, `rivet`).

## Stream B — Army & camp models  (`Tools/blender/build_army.py` → `Assets/Siege/Models/Army.fbx`)
Life-size enemy footman (~1.8 m), plus siege kit and camp. Objects (pivots in Blender coords):
| name | pivot | notes |
|---|---|---|
| `Soldier_Hips` | (0,0,0.95) | belt/tassets |
| `Soldier_Torso` | (0,0,1.05) | padded gambeson + **red tabard** (enemy colour, no crosses/religious symbols), mail collar |
| `Soldier_Head` | (0,0,1.50) | face (skin `swatch(14,1)`-ish) + steel kettle hat with brim |
| `Soldier_ArmL` / `Soldier_ArmR` | (∓0.24,0,1.42) shoulders | arms hang straight down (−Z), gloves at the end |
| `Soldier_LegL` / `Soldier_LegR` | (∓0.10,0,0.92) hips | legs hang down, boots |
| `Soldier_Sword` | (0.24,0.06,0.78) grip in right hand | blade points forward/up (+Y,+Z ~45°), ~0.9 m |
| `Soldier_Spear` | (0.24,0.06,0.80) grip | vertical spear ~2.4 m, steel head up |
| `Soldier_Shield` | (−0.33,0.10,1.0) on left forearm | round wooden shield ~0.6 m, red/cream, faces +Y |
| `Ladder` | (0,0,0) bottom centre | two rails along +Z, 7.5 m long, 0.6 m wide, rungs every ~0.35 m |
| `Ram` | (0,0,0) log centre | log along +Y, 6 m, r≈0.28, iron-shod head at +Y end, iron bands, 3 pairs of rope handles |
| `Tent` | ground centre | round pavilion tent ~4 m Ø, ~4 m tall, red/cream stripes, pole + pennant |
| `Tent_Ridge` | ground centre | ridge tent ~4×3 m, ~2.8 m tall |
| `Campfire` | ground centre | stone ring + logs |
| `Crate_Stack` | ground centre | supply crates/sacks |
Lay the objects out apart from each other in the file (e.g. offset along X) — their pivots must stay where
listed relative to their own geometry (use `join(..., pivot)`; the integrator re-centres instances by name).
Budgets: soldier body + one weapon + shield ≤ 2,000 tris (it is instanced ~25×); ladder ≤ 500; ram ≤ 1,800;
tents ≤ 1,800 each.

## Stream C — `Scripts/Game/Trebuchet.cs`  (new; replaces `Catapult.cs`, which the integrator deletes)
`public class Trebuchet : MonoBehaviour, IDamageable` — keep the exact public surface the rest of the game
uses today on `Catapult` (read `Scripts/Game/Catapult.cs` and mirror it):
- Add an `AimTarget` (Stream E's class, see below) to the body centre at runtime in `Awake` (offset at the frame's
  mid-height) so the player's aim assist can lock onto the trebuchet.
- Rig fields (assigned by the builder): `Transform body, arm, counterweight, windlass, slingTip, pouch, ropeWinch,
  ropeArm, banner; LineRenderer sling, rope; MeshFilter frame; Knight knight; Soldier[] crew; TNTBarrel[] barrels;
  Material debrisMaterial; Fireball fireballPrefab; GameObject landingMarkerPrefab;`
- `float Health {get;}`, `float MaxHealth => Tune.CatapultHealth`, `bool Alive`, `Vector3 AimCenter`,
  `event Action<float, Vector3, bool> Hit`, `event Action Destroyed`,
  `void Begin(Castle castle, Transform cam)`, `void Stop()`, `IEnumerator RollIn(float distance, float duration)`
  (for a trebuchet: an "assemble" intro — it rises out of the ground with dust/thuds, crew run in; the signature
  stays so GameManager is unchanged), `void TakeHit(HitInfo)`, `void ApplyDamage(HitInfo, bool knightHit)`.
- Mechanics (arm angle = rotation about the arm's local X; 0 = horizontal as modelled, positive tips the long
  end down at the back):
  - Loaded pose `+40°` (long end down near the trough, pouch lying in the trough with the fireball in it).
  - Fire: counterweight drops; arm accelerates (counterweight-driven, not linear) through vertical to ~`−100°`,
    then a damped oscillation that settles around `−90°` (long arm pointing up). Counterweight always hangs
    world-down from its hinge with pendulum lag/swing.
  - Sling: `sling` LineRenderer from `slingTip` to `pouch`; the pouch is a simulated point (verlet/pendulum,
    rope length ~4.5 m) dragged by the tip — it whips round during the swing. The fireball rides in the pouch and
    is released at the right moment (pouch moving up/forward ~45°) with a ballistic velocity to the target
    (reuse `Release()` logic from Catapult; `Fireball.Launch` signature unchanged).
  - Reload: crew (Soldier `Haul` state) turn the windlass; `rope` LineRenderer from `ropeWinch` to `ropeArm`
    goes taut; the arm is winched back down to +40° in ratchet steps; knight commands (Knight `Pull` to give the
    release order). Cadence from `Tune` (slower is fine: it's artillery support now).
  - Same targeting/near-miss/miss logic, Sfx/Haptics/ScreenFX/VFX cues as Catapult (launch thump + whoosh,
    slam, dust at the base). Damage stages (smoke ≤66 %, fire ≤33 %) and `BreakApart` (frame shatters, arm and
    counterweight topple as rigid bodies, crew/knight ragdoll, TNT barrels detonate) as in Catapult.
- `Soldier` is Stream D's class; only use `Soldier.SetState(Soldier.State.Haul|Idle|Cheer)`,
  `Soldier.Kill(Vector3 from, float force)` and `Soldier.Alive`.

## Stream D — Soldiers & waves  (`Scripts/Game/Soldier.cs`, `Scripts/Game/WaveManager.cs`, optional helpers in new files)
- `public class Soldier : MonoBehaviour, IDamageable`:
  - Rig: `Transform hips, torso, head, armL, armR, legL, legR, weapon, shield;` (built by integrator from Army.fbx
    names above; root at the feet, facing +Z). `enum State { Idle, March, Carry, Haul, Climb, Attack, Ram, Cheer, Dead }`,
    `void SetState(State s)`, `State CurrentState`, `bool Alive`, `float health = 1`, `event Action<Soldier> Died`.
  - Procedural animation per state (march cycle tied to distance travelled, carry = arms up holding a pole,
    haul = lean back pulling rhythmically, climb = alternating arms/legs, attack = weapon swing at the gate,
    ram = rock back and lunge in sync with a phase the squad sets, cheer). Small per-soldier variation
    (speed, phase, height ±4 %). Add an `AimTarget` (offset at torso height) in `Awake`; disable it on death.
  - Movement: `void MoveTo(Vector3 worldTarget, float speed)`, `bool Arrived`, `void Stop()`; follows the ground by
    raycasting down onto Environment each frame; faces travel direction; simple separation from nearby soldiers.
  - `TakeHit(HitInfo)`: any arrow or explosion kills (`health` ≤ 0). `Kill(Vector3 from, float force)`:
    state Dead, topple/ragdoll (add Rigidbody + collider, impulse away from `from`), drop weapon, grunt
    (`Sound.KnightGrunt`, higher pitch), `Haptics.HitConfirm()` only when killed by an arrow, then after ~4 s sink
    into the ground and destroy. Fires `Died`.
- `public class WaveManager : MonoBehaviour`:
  - Fields: `Soldier soldierPrefab; GameObject ladderPrefab, ramPrefab; Transform camp;` `int Wave {get;}`,
    `int WaveCount = 3`, `int AliveCount {get;}`, `int Infiltrated {get;}`, events `Action<int> WaveStarted`,
    `Action AllWavesCleared`, `Action GateBreached`.
  - `void Begin(Castle castle, Transform camp)` / `void Stop()`.
  - Waves: W1 = 8 swordsmen/spearmen + 1 ladder team (2 carriers who then climb); W2 = 10 + a ram team (4) + 1
    ladder team; W3 = 14 + ram team + 2 ladder teams. Short breather between waves (~6 s). Spawn staggered out of
    the camp, march toward the castle front in loose ranks (lateral spread), path straight across the floor to a
    staging point ~4 m in front of the gate (gate = `castle.pieces` entry with `kind == PieceKind.Gate`; use its
    `WorldBounds` and the castle's forward), then:
    - foot soldiers crowd the gate (spread along its front) and hack at it: each attacker every ~1.2 s calls
      `gate.TakeHit(new HitInfo { damage = 0.04f, point = <on the gate surface>, direction = <toward gate>, force = 1f, source = HitSource.Fireball })`
      (`CastlePiece.TakeHit` ignores `HitSource.Arrow`; `Fireball` source applies damage + shudder + stone chips).
    - ladder teams carry the ladder (Soldier `Carry`) to the gate wall left/right of the arch, raise it (rotate up
      against the wall over ~1 s), climb (`Climb`); a climber reaching the top increments `Infiltrated`, damages the
      gate piece (0.3) and disappears over the parapet. Shooting climbers knocks them off.
    - ram team carries the ram to the gate and rams on a ~1.6 s rhythm: big gate damage per hit (0.35), dust,
      `Sound.Thud`/`Sound.WoodBreak`, `ScreenFX.Shake`, `Haptics.CastleImpact(0.4)`; if ram carriers die the ram
      drops (needs 2 to keep ramming; others pick it up).
  - Gate destroyed (`!gate.Alive`) or `Infiltrated >= 6` → `GateBreached` (soldiers cheer and stream through the
    gap into the courtyard). All waves spawned and everyone dead → `AllWavesCleared`.
  - Expose `IEnumerable<Soldier> Soldiers` for aim assist/HUD.

## Stream E — Combat realism (`Scripts/Core/Damage.cs`, `Scripts/Player/Arrow.cs`, `Scripts/Player/BowController.cs`, `Scripts/Game/Fireball.cs`, `Scripts/Game/Knight.cs`)
- `Tune`: `Gravity = 13f`; `ArrowMinSpeed = 30f`, `ArrowMaxSpeed = 62f` (m/s; real longbow ~55–60);
  arrow damage scales with draw: `ArrowDamageMin = 0.6f`, `ArrowDamageMax = 1.6f`; add constants other streams
  need only if they're truly shared. Keep all existing members (others compile against them).
- `HitInfo`: add `public Transform part;` (collider transform that was hit) and `public float power;` (0–1 draw).
- `Arrow.Launch(Vector3 velocity, float power)` (keep a 1-arg overload calling power=1). Damage =
  `Lerp(ArrowDamageMin, ArrowDamageMax, power)`; hit-zone multipliers: knight `Head` ×2.5, trebuchet arm/frame ×1,
  soldiers die on any hit. Arrow size is real (~0.9 m) — scale only via `BowController.arrowScale`.
- **Shoot fireballs down**: every flight step test the arrow segment against `Fireball.Active` (distance from
  fireball centre to segment < ~1.4 m) → `Fireball.ShootDown(Vector3 point)`: mid-air burst
  (`VFX.Explosion` smaller + shower of embers), no castle damage, `Sfx.Explosion`, `Haptics.HitConfirm`,
  `BowController.NotifyHit`, fireball removed (and its landing marker). Near-miss fireballs can be shot too.
- Aim assist (BowController) must not reference Catapult/Trebuchet/Soldier types. Create
  `Scripts/Player/AimTarget.cs`: `public class AimTarget : MonoBehaviour { public static readonly List<AimTarget> All;
  public Vector3 offset; /* local */ public bool ballistic; /* falls under gravity (fireballs) */
  public Vector3 Point {get;} public Vector3 Velocity {get;} /* measured per frame */ }` registering in
  OnEnable/OnDisable. Other streams/integrator add it (`AddComponent<AimTarget>()`) to: trebuchet body centre,
  knight torso, TNT barrels, each soldier (torso height), each launched fireball (`ballistic = true`; Stream E does
  this inside `Fireball.Launch`). BowController locks onto the candidate nearest the crosshair inside ~4°, and
  aims **with lead** (predict where it will be when the arrow arrives; for `ballistic` targets include gravity).
  The arrow must still land where the crosshair is when nothing is locked (ballistic solve already exists).
- Keep the arrow realistic: no glowing tracer; faint streak only; spins in flight (already).
- `Knight.TakeHit`: if `hit.part` is the head (or a child of it) apply the head multiplier.
- Do not touch other files; the integrator wires everything else.
