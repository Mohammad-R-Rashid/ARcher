# Hooking the AR app up to the gloves

**No laptop in the real game.** Glove 1 creates its own WiFi network and is the hub: it relays every glove
event to the phones and every phone message to the gloves. (A laptop running `server.py` is only for testing.)

## 1. Connect
- Each phone joins WiFi **`BowDuel`**, password **`bowduel26`** (it has no internet; iOS will say so, that's fine).
- The app opens a WebSocket to **`ws://192.168.4.1/ws`** (fixed address, no discovery needed).
- Test page in any browser on BowDuel: **http://192.168.4.1**

Every message, both ways, is one JSON object. `p` is the player (1 or 2).

## 2. What the app receives
| `t` | Fields | Do this |
|---|---|---|
| `release` | `power` 0–1, `pitch`, `roll` (deg, may be null), `held_ms` | **Fire the arrow.** Power = arrow speed |
| `draw_start` | | Start the bow-draw animation |
| `draw` | `power` 0–1 (every 50 ms) | Animate the string being pulled |
| `cancel` | | Fist was too short: stop the draw animation, no arrow |
| `hb` | `bend`, `state`, `pitch`, `roll`, `imu` (4×/s) | Optional: show the glove is alive |
| `zero` | `pitch0`, `roll0` | That glove finished recentering |
| `round_start` | | Someone pressed Start round: show the countdown |

Aim: **left/right comes from the phone** (where the camera points); the glove can't sense it.
Up/down: use the glove `pitch` from `release` if it isn't null, otherwise the phone's tilt.

## 3. What the app sends
| Message | Effect |
|---|---|
| `{"t":"cmd","p":1,"cmd":"zero"}` | Recenter glove 1: player holds the bow level and still for ~4 s |
| `{"t":"round_start"}` | Tell the other phone to show the countdown too |
| `{"t":"hit","p":1,"target":2,"dmg":20}`, `{"t":"reset"}` | Anything else is relayed to every phone: use it to keep both phones' game state in sync |

## 4. Round start (countdown + recenter)
1. Player taps **Start round** (also put a **Recenter** button somewhere in the UI that sends just the `zero` cmd).
2. App sends `round_start`, then `zero` for p 1 and p 2.
3. Show "Hold your bow level and still" with 3…2…1 over **4.5 s**. The glove needs about 4 s and ignores
   draws while it measures. Ignore `release` until the countdown ends.
4. Show GO. (`zero` replies arrive around 4 s; you can wait for them instead of the timer.)

The test page (http://192.168.4.1, "Start round") does exactly this; open it next to the app to compare.

## 5. Swift (URLSessionWebSocketTask, no packages)
```swift
let ws = URLSession.shared.webSocketTask(with: URL(string: "ws://192.168.4.1/ws")!)
ws.resume()

func listen() {
    ws.receive { result in
        if case .success(.string(let text)) = result,
           let m = try? JSONSerialization.jsonObject(with: Data(text.utf8)) as? [String: Any],
           m["t"] as? String == "release" {
            let power = m["power"] as? Double ?? 0.5
            let pitch = m["pitch"] as? Double          // nil without the IMU
            DispatchQueue.main.async { /* fire arrow for player m["p"] */ }
        }
        listen()                                       // keep receiving
    }
}
listen()

func recenter(player: Int) {
    ws.send(.string("{\"t\":\"cmd\",\"p\":\(player),\"cmd\":\"zero\"}")) { _ in }
}
```
Info.plist: add `NSLocalNetworkUsageDescription` (iOS asks the user for local-network access the first
time). If the plain `ws://` connection is still refused, also add `NSAppTransportSecurity` →
`NSAllowsLocalNetworking = YES`.

## 6. Without hardware
The test page's **Hold to draw** buttons (or keys A / L) send the same `draw_start` / `draw` / `release`
messages through the server, so the app can be built and tested before the gloves are ready.
