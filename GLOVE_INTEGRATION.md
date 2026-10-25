# Bow glove ↔ Siege AR integration: status

Where the glove (XIAO ESP32-C3/S3 with a flex sensor and an LSM6DSO32 IMU) and the Unity AR game stand, what has
been decided, and what's left. Branch: `feature/glove_motion`. Glove details: `glove/README.md`, protocol:
`glove/HANDOFF.md`.

## How it fits together

```
glove (Arduino firmware)                         phone (Siege AR, Unity)
  flex: fist = draw, open = release   ──UDP──▶   GloveUdp / GloveLink  (port 9000)
  IMU:  pitch (vertical aim only)     JSON per                │
                                      datagram                ▼
                                     ◀──UDP──    bow input → BowController (arc preview, fire)
                         commands: zero / cal / ping (with id)
```

- **The phone hosts the WiFi.** It turns on its 2.4 GHz Personal Hotspot, its glove joins it, and the glove sends
  every event to the hotspot's router address (the phone) on UDP 9000. The app replies to the address the
  datagrams came from. One glove per phone, so each phone hears only its own glove.
- **Aim:** left/right always comes from where the phone points. Up/down comes from the phone (aim assist on) or
  the glove's IMU pitch (raw, no assist). See "Aim modes" below.

## Decisions (and why)

| Decision | Why |
|---|---|
| **UDP**, not TCP/WebSocket | Lowest latency, and a lost packet doesn't stall the ones behind it. With TCP a single loss holds back the next `release`, so the shot fires late. Unity games on native platforms use UDP (Unity Transport does). WebSocket is mainly for WebGL builds. |
| UDP plus **a small reliability layer** | Frequent messages (`draw` every 50 ms, `hb` 4×/s) can be lost harmlessly. The one-off ones (`release`, `cancel`, command replies) are sent 3× and deduped by `shot`/`id`, and commands are retried until they're answered. |
| **Phone hotspot**, not glove-hosted WiFi | Tested end to end on hardware, the phone keeps cellular internet (needed for 2-player sync later), and no firmware change is needed. Downsides: each glove is flashed with its phone's hotspot name and password, the iPhone hotspot settings screen must stay open while the glove joins, and Apple doesn't officially support servers on a hotspot. Fallback if iOS breaks this: the glove hosts its own WiFi (needs the firmware change below). |
| **Arduino firmware** (`glove/arduino/glove/glove.ino`) | Phones couldn't join a network hosted by the MicroPython firmware, but an Arduino-hosted one worked at once. `glove.py` is kept for bench tests only. |
| **JSON** messages, parsed with Newtonsoft (Unity package) | Human-readable, already what the firmware sends. Unity's JsonUtility can't represent `null` (e.g. `pitch: null` without an IMU). |
| **Minimum draw = power 0.3** | The glove cancels weaker draws (`MIN_DRAW_POWER`), the same number as the game's `BowController.minDrawToFire`. Measured in power, not ms, so it follows `FULL_DRAW_MS`. |
| **Two aim modes** + **arc preview required** | *Phone aim*: the glove only draws and looses; the phone aims with assist. *Glove pitch*: the IMU sets elevation raw (no assist), which feels more like real archery, and the dotted arc shows where the shot lands. |
| **Single-player MVP**, 2-player later | Keep the glove input player-aware (`p` field) and the network link separate from the bow input, so 2-player can be added. Syncing two phones would go over the internet or GameKit. |

## Done

| What | Where | Commit | Verified |
|---|---|---|---|
| Glove sends `pitch` with every `draw` message (same 100 ms lookback as `release`, so the preview matches the shot) | `glove.py`, `glove.ino` | `99314a8`, `f843f94` | — |
| Arduino firmware, phone-hotspot UDP design, two-glove test page | `glove/arduino/`, `glove/index.html` | `bdd468a` | **Hardware:** UDP both ways between phone and ESP, mini game played |
| Draws below power 0.3 cancel instead of releasing | `glove.ino`, `glove.py`, test page | `8ce5434` | Code check only |
| UDP reliability in the firmware: `shot` numbers, 3× `release`/`cancel`/replies, command `id`s run once, `zero` always replies (`ok:false` on failure) | `glove.ino`, `HANDOFF.md` | `3e1222f` | Compiles for C3 and S3 (ESP32 core 3.3.8, no warnings). **Not yet run on hardware.** |
| **Component 1: phone-side UDP link.** `GloveUdp` (plain C#) + `GloveLink` (Unity wrapper). Listens on 9000, learns the glove's address, drops repeats (also after a glove reboot), retries commands until answered (`cmd_timeout` after 3 tries), pings on first contact, recreates the socket after errors and app suspension. | `Assets/Siege/Scripts/Glove/` | `8357dda` | 14 dotnet tests vs a fake glove over real UDP (`dotnet test Tools/glove-tests`). **Not yet compiled in Unity or run on a phone.** |

## Still to do (MVP)

Build and verify **one at a time**. Numbers match the original plan.

| # | Component | Notes |
|---|---|---|
| 2 | **Draw-state logic** | Turns glove events into per-frame bow state. A draw must be visible ≥1 frame before its release or `BowController` drops the shot. **Lost release:** if all 3 copies are lost, end the draw (no arrow) when a different `shot` arrives or an `hb` says `state:"open"` for the same shot. |
| 3 | **Glove bow input** (`IBowInput`) | Uses `GloveLink.Message`. Two aim modes: phone (`AimDirection = null`) and glove pitch (phone heading + glove pitch). Smooths pitch for the preview but fires with the exact release pitch. Recenter = `GloveLink.SendCommand("zero")`, done when `zero` arrives (`ok`) or `cmd_timeout`. |
| 4 | **Bow changes** (`BowController`) | ⚠️ Fire and check `minDrawToFire` with the glove's **exact** power, not the smoothed `CurrentDraw`, or glove releases just above 0.3 get dropped. Skip the aim-assist lock in glove-pitch mode. |
| 5 | **Arc preview** | Already in `BowController.UpdateTrajectory()`, but currently switched off (`BowController.cs`, `trajectory.enabled = false`). Re-enable it, then tune readability at AR scale on a device. |
| 6 | **Wiring** (`GameManager`) | Create `GloveLink`, switch between glove and touch input (never mid-draw, leave AutoPilot alone), recenter during the castle-build intro in glove-pitch mode. |
| 7 | **HUD** | Glove status (connected / IMU), aim-mode toggle, recenter button. |
| 8 | **iOS build** (`Assets/Siege/Editor/iOSBuild.cs`) | Add `NSLocalNetworkUsageDescription` to Info.plist. `GloveUdp`'s first-contact ping should make iOS ask for local-network access early. Plain `ws://` permissions aren't needed (no WebSocket). |
| — | **Firmware: reply to the last sender** (proposed, not started) | The glove sends events to whoever last sent it a command (for ~10 s), else to the phone. Plus app side: `GloveUdp` pings a configured `gloveAddress` until heard. Enables Unity-editor testing with the real glove and the glove-hosted WiFi fallback. |

## Housekeeping

- **Unity `.meta` files:** `GloveUdp.cs` and `GloveLink.cs` were committed without `.meta` files (written outside
  Unity). The first person to open the project in Unity should commit the generated `.meta` files right away, so
  everyone shares the same script GUIDs.
- **Hardware checks pending:**
  - Each `release` should arrive 3× with the same `shot`.
  - Sending `{"t":"cmd","cmd":"zero","id":1}` again *after* the reply should resend the reply without recentering.
- The root `README.md` "Hooking up the IMU glove later" section predates this work. Update it once components 3 and 6 land.
- An earlier, untested draft of components 2–7 (written for the old WebSocket design) exists only as a local
  git stash on Jay's machine. It's reference material, not on GitHub.
