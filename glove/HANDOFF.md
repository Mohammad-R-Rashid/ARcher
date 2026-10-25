# Hooking the AR app up to a glove

**One glove per phone, and the phone is the host.** The phone turns on its hotspot, its glove joins it,
and the glove sends every event straight to the phone. No laptop, no server, no other network.

## 1. Setup (once per phone)
- Turn on the phone's **hotspot on 2.4 GHz** (iPhone: Personal Hotspot + **Maximize Compatibility**;
  Samsung: Mobile Hotspot, band **2.4 GHz**). The glove can't see 5 GHz.
- Each glove is flashed with its phone's hotspot name and password:
  `python3 arduino/flash_glove.py --board s3 --player 1 --ssid "<hotspot name>" --password "<password>"`
- Power the glove from a **USB power bank** into its USB-C port. It joins within a few seconds and starts
  sending. (iPhone: keep the Personal Hotspot screen open while the glove joins, and the iPhone must not be
  joined to any WiFi network itself, or its hotspot stops letting devices in.)

## 2. Receive events: listen on UDP port 9000
The glove sends each event as **one UDP datagram** containing **one JSON object** to the phone (the
hotspot's router address) on port **9000**. Listen on that port; no address needs to be configured.

| `t` | Fields | Do this |
|---|---|---|
| `release` | `shot`, `power` 0–1, `pitch`, `roll` (deg, may be null), `held_ms` | **Fire the arrow.** Power = arrow speed. **Sent 3×** (see below) |
| `draw_start` | `shot` | Start the bow-draw animation |
| `draw` | `shot`, `power` 0–1, `pitch` (deg, may be null), every 50 ms | Animate the string; preview the arc (`pitch` uses the same 100 ms lookback as `release`, so the preview matches the shot) |
| `cancel` | `shot` | Draw too weak (power < 0.3): stop the draw animation, no arrow. **Sent 3×** |
| `hb` | `bend`, `state`, `shot`, `pitch`, `roll`, `imu` (4×/s) | Glove is alive (show "connected") |
| `zero` | `ok`, `pitch0`, `roll0`, `id` | Recenter finished (`ok:false` = it couldn't, e.g. no IMU). **Sent 3×** |
| `calib` | `open`, `fist` (mV), `id` | Flex calibration finished. **Sent 3×** |
| `pong` | `id` | Answer to `ping` |
| `log` | `msg` | Status text. Show `CALIBRATE: ...` / `ZERO AIM: ...` messages as prompts to the player |

Every message also has `p` (player 1 or 2). Aim: **left/right comes from the phone** (where the camera
points); up/down from the glove's `pitch` in `release` if it isn't null, else the phone's tilt.

### Lost packets (UDP)
UDP can drop a datagram. `draw` (every 50 ms) and `hb` (4×/s) don't matter: the next one replaces it.
The one-off messages do, so:
- **`release`, `cancel`, `zero`, `calib`, `pong` arrive 3 times**, ~20 ms apart. Act on the first copy, ignore
  copies with the same key: `shot` for release/cancel, `id` for command replies.
- **`shot`** numbers each draw (starts at 1 after the glove boots; treat any *change* as a new draw). If all
  3 copies of a release are lost, the next `draw_start`/`draw` with a different `shot`, or an `hb` with
  `state:"open"` and the same `shot`, means that draw is over: drop it (no arrow) so the bow doesn't stay drawn.

## 3. Send commands: reply to the glove, UDP port 9000
Send to **the address the glove's datagrams came from**, port 9000. Plain text is enough:

| Send | Effect |
|---|---|
| `zero` | **Recenter**: player holds the bow level and still ~4 s (glove ignores draws meanwhile) |
| `cal` | Recalibrate the flex sensor (straight, then bend; prompts arrive as `log` messages) |
| `ping` | Glove answers `pong` |

**The app should send commands as JSON with an `id`**, and resend until the reply comes:
`{"t":"cmd","cmd":"zero","id":7}` → the glove's reply (`zero` / `calib` / `pong`) carries `"id":7`.
- A command can get lost too: if there's no reply, send the **same** command with the **same** `id` again
  (every ~1 s for `ping`; `zero` takes ~4 s, so wait ~5 s before the first retry).
- The glove runs each `id` once. A repeat while it's still running is ignored; a repeat after it finished
  gets the same reply again (it does **not** recenter twice). Use a new `id` for each new command.
- Plain text (`zero`) and JSON without `id` still work (the test page uses them), but get no retry protection.

**Round start:** show "Hold your bow level and still" with 3…2…1 over **4.5 s**, send `zero` at the start,
ignore `release` until GO.

## 4. Swift (Network framework, no packages)
```swift
import Network

let listener = try! NWListener(using: .udp, on: 9000)
var glove: NWConnection?                       // the glove's address, learned from its first datagram

listener.newConnectionHandler = { conn in
    glove = conn
    conn.start(queue: .main)
    func receive() {
        conn.receiveMessage { data, _, _, _ in
            if let data, let m = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
               m["t"] as? String == "release" {
                let power = m["power"] as? Double ?? 0.5
                let pitch = m["pitch"] as? Double      // nil without the IMU
                // fire the arrow
            }
            receive()
        }
    }
    receive()
}
listener.start(queue: .main)

func recenter() { glove?.send(content: "zero".data(using: .utf8), completion: .contentProcessed { _ in }) }
```
Info.plist: add `NSLocalNetworkUsageDescription` (iOS asks for local-network access once).

**Android (Kotlin):** `DatagramSocket(9000)`, `receive()` in a background thread, parse the JSON, and reply
with `socket.send(DatagramPacket(bytes, bytes.size, packet.address, 9000))`.

## 5. Testing without the app
The glove also serves the test page: open **http://<glove's IP>** in the hotspot phone's browser (the glove
prints its IP on USB). Both gloves on one hotspot: `http://<glove 1 IP>/?gloves=<glove 2 IP>` shows the
two-player duel. The test page's **Hold to draw** buttons (keys A / L) simulate a glove without hardware.

## 6. Syncing the two phones
Each phone only hears its own glove. The two phones keep their cellular internet (they host the hotspot),
so the app can sync game state (hits, HP, round start) between phones over the internet or GameKit.
