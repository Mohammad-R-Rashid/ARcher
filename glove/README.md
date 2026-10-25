# Bow Glove Kit

## The gloves (XIAO ESP32-S3 / C3): Arduino firmware, one glove per phone

The real game uses `arduino/glove/glove.ino`. Each phone turns on its **hotspot on 2.4 GHz** (iPhone: Personal Hotspot
+ Maximize Compatibility; Samsung: band 2.4 GHz); its glove joins it and sends every event as JSON over **UDP port 9000**
to the phone. How the AR app receives them: `HANDOFF.md`.

```bash
# once: brew install arduino-cli, the esp32 core (https://espressif.github.io/arduino-esp32/package_esp32_index.json)
python3 arduino/flash_glove.py --install-libs --board s3 --player 1 --ssid "<hotspot>" --password "<password>"
python3 arduino/flash_glove.py --board c3 --player 2 --ssid "<other phone's hotspot>" --password "<password>"
# a glove can know several phones: repeat --ssid/--password; it joins whichever hotspot is on (strongest wins)
```
- Power: a USB power bank in the glove's USB-C port (see "Power" below).
- If a glove won't join, its USB serial output lists the hotspots it can see and the WiFi driver's refusal reason.
- First flash of a board that still runs MicroPython: add `--erase`; an S3 may need **BOOT held while plugging in**,
  then **RESET** after the upload.
- First boot: flex calibration (S3 LED solid = straight, blinking = bend; prompts also arrive as `log` messages).
  Saved on the board.
- Testing without the app: open `http://<glove IP>` on the hotspot phone (the glove prints its IP on USB serial);
  `http://<glove 1 IP>/?gloves=<glove 2 IP>` shows both gloves on one page.
- `--own-network` makes a glove host WiFi `BowDuel-P<n>` (192.168.4.1) itself, for tests without a hotspot.
- `arduino/ap_test/` is a 20-line WiFi check (hosts `BowDuelTest`).

Why Arduino: no phone (iPhone or Android) could join a WiFi network hosted by the MicroPython firmware below, while an
Arduino-hosted one worked immediately. The MicroPython version (`glove.py`, Pico over USB + `server.py`) is kept for
bench tests.

---

## MicroPython / Pico bench setup (older; step by step)

Goal: Pico with flex sensor (+ optional IMU) plugged into this Mac over USB → `server.py` → test page in a browser shows the glove live and shoots arrows.

### 1. Python tools
```bash
python3 -m pip install --user aiohttp pyserial mpremote
```
Always call mpremote as `python3 -m mpremote ...` (the plain `mpremote` command is usually not on PATH on macOS).

### 2. Put MicroPython on the Pico (only if it doesn't have it yet)
Check first: `python3 -m mpremote devs` — if the Pico shows as `MicroPython Board`, skip this step.
1. The board is an RP2040 Pico (chip says RP2-B1). Plain Pico → download the latest `.uf2` from https://micropython.org/download/RPI_PICO/ ; Pico W → https://micropython.org/download/RPI_PICO_W/
2. Unplug the Pico, hold **BOOTSEL**, plug it in, release. A drive **RPI-RP2** appears.
3. `cp ~/Downloads/RPI_PICO-*.uf2 /Volumes/RPI-RP2/` — the drive disappears when done.

### 3. Wiring (physical pin numbers count from the USB end; pin 1 = top-left with USB on top)
| Part | Connection |
|---|---|
| Flex sensor | 3V3(OUT) pin 36 → flex → node; node → **GP26 (pin 31)**; node → **10k resistor** (brown-black-orange) → GND pin 38 |
| IMU (optional) LSM6DSO32 | VIN → 3V3(OUT) pin 36, GND → GND, **SDA → GP16 (pin 21)**, **SCL → GP17 (pin 22)** |

The flex sensor has no polarity. The glove still works if the IMU is missing or flaky.

### 4. Quick sensor checks (optional, before the full firmware)
```bash
python3 -m mpremote exec "from machine import ADC; import time
a=ADC(26)
for i in range(50): print('%.3f V' % (a.read_u16()*3.3/65535)); time.sleep(0.1)"
```
Expect ~0.9–1.5 V flat that changes when bent. Stuck near 3 V = resistor/GND not connected or wrong resistor value. Stuck near 0 V = flex not connected.

### 5. Upload the firmware
```bash
cd <this repo>
python3 -m mpremote cp lsm6dso32.py :lsm6dso32.py
python3 -m mpremote cp glove.py :main.py
python3 -m mpremote reset
```
On first boot the glove calibrates: Pico LED **solid** = hold hand OPEN, **blinking** = make a FIST (about 6 s total). Saved to `calib.json` on the Pico.

### 6. Run the server
Make sure nothing else (mpremote, Thonny) is connected to the Pico, then:
```bash
python3 server.py
```
Open **http://localhost:8000**. "Player 1 glove: online (usb)" should turn green; flexing moves the bars; fist → open fires an arrow. Click **Recalibrate flex** (asks first) to redo the flex calibration (watch the log panel for prompts).

### 7. Test from a phone (optional)
Phone and Mac on the same network (use a phone hotspot — campus WiFi usually blocks device-to-device). Open the `http://<ip>:8000` address that `server.py` prints.

### Troubleshooting (things we already hit)
| Symptom | Fix |
|---|---|
| `zsh: command not found: mpremote` | use `python3 -m mpremote` |
| `could not enter raw repl` | MicroPython not flashed (step 2), or Thonny/server holding the port, or a running `main.py` — try `python3 -m mpremote connect auto soft-reset` |
| `I2C scan: []` | wrong pins (use GP numbers, not physical numbers), IMU not powered, header pins not soldered |
| IMU found at 0x6a but `OSError: [Errno 5] EIO` | loose/unsoldered IMU pins; firmware already uses SoftI2C at 100 kHz; glove works without IMU |
| Server says "no glove on USB" | Pico unplugged, or `mpremote` still running in another terminal |
| Calibration keeps failing | open and fist read the same → check flex wiring (step 4) |

```
glove (Pico: USB  |  XIAO ESP32-S3: WiFi UDP :9000)
        │
        ▼
server.py on a laptop  ──WebSocket ws://<laptop-ip>:8000/ws──▶  phone AR app(s) + test page
```

| File | Runs on | What it does |
|---|---|---|
| `glove.py` | Pico / ESP32 (as `main.py`) | Reads the flex sensor (+ optional IMU), detects draw/release, sends JSON events |
| `lsm6dso32.py` | Pico / ESP32 | IMU driver (optional - glove works without it) |
| `server.py` | Laptop | Relays glove events to every connected phone/browser |
| `setup_xiao.py` | Laptop | Flash + configure + upload a XIAO ESP32-S3 (or C3) glove in one command |
| `index.html` | Browser (served by server.py) | Live sensor readout + 2D mock duel + simulate buttons + Start round countdown |
| `HANDOFF.md` | (doc) | How the AR app connects: messages, countdown/recenter flow, Swift snippet |

## XIAO with MicroPython (superseded by the Arduino firmware above)

**Network:** glove 1 creates its own WiFi `BowDuel` (password `bowduel26`); glove 2, the laptop and the phones join it.
No hotspot or router needed (an iPhone hotspot gave the Mac no IPv4 address, so it couldn't reach the gloves).
```bash
python3 setup_xiao.py --player 1 --host              # glove 1 (S3) creates BowDuel
python3 setup_xiao.py --player 2 --board xiao_c3     # glove 2 (C3) joins it
```
Then join BowDuel on the laptop, run `python3 server.py`, and open the test page.

**One command** (board plugged into the laptop by USB-C; `firmware/` already holds MicroPython v1.29.0 for the S3 and C3,
`pip install --user esptool` is needed once):
```bash
python3 setup_xiao.py --player 1 --ssid "Hotspot Name" --password "hotspot-pass" --flash
```
`--flash` erases the board and installs MicroPython (first time). Without it, it only re-uploads the glove code and
keeps the saved calibration. It sets the config below in the uploaded copy; your `glove.py` stays as is.
If the board isn't found: hold **BOOT**, plug it in, release, run again. `server.py` leaves XIAO USB ports alone.

**Hotspot gotchas:** the ESP32-S3 only does **2.4 GHz WiFi**. On an iPhone hotspot turn on
**Settings → Personal Hotspot → Maximize Compatibility**, or the glove never sees the network. The name must match
exactly: iPhones use a curly apostrophe (`Bilal’s iPhone`, not `Bilal's iPhone`), so copy it or rename the phone.

For a XIAO ESP32-**C3** add `--board xiao_c3`. By hand instead: in `glove.py` set `BOARD = "xiao_s3"`, `TRANSPORT = "udp"`, `PLAYER = 1` or `2`, `WIFI_SSID`, `WIFI_PASS`.
Leave `SERVER_IP = None`: the glove finds `server.py` by itself (the server broadcasts "I'm here" on UDP 9001 once a
second; the glove takes the sender's address). Set an IP only if discovery fails on some network.
Laptop, phones and gloves must be on the same network - use a phone hotspot (campus WiFi usually blocks device-to-device traffic).
Attach the XIAO's antenna or WiFi will be very weak.

Untested until the board arrives: MicroPython's WiFi + discovery on the ESP32 (the server side and the protocol were
tested with a stand-in glove on a Mac). The test page shows calibration prompts as a banner over the game
("keep the flex STRAIGHT" / "BEND IT"); the S3's orange user LED (GPIO21) shows them too. The C3 has no user LED.

Wiring on the XIAO ESP32-S3 (same D-labels on the C3, different GPIOs):
- Flex: XIAO `3V3 -> flex -> D0/A0 (GPIO1) -> 10k -> GND`. The flex can sit on its own rails near the finger, but those
  rails must be wired to the XIAO's **3V3 and GND**, never to the battery (battery voltage drifts and a full LiPo is 4.2 V).
- IMU: `VIN -> 3V3, GND -> GND, SDA -> D4 (GPIO5), SCL -> D5 (GPIO6)`.
- Long breadboards often split their power rails in the middle: check continuity.
Recalibrate the flex (`Recalibrate flex`) and recenter after moving to the XIAO: its ADC reads differently from the Pico.

### Power: a USB power bank
What works: **a portable USB charger (power bank) into the glove's USB-C port**, the same power as a laptop's USB.
Tried on 2026-10-25 and did not keep the glove running: a LiPo on the XIAO's 5V pin (it never finished joining
WiFi; WiFi draws ~300 mA bursts and a 3.7-4.2 V cell leaves the regulator no headroom), and a LiPo through an Adafruit
PowerBoost 500C (not resolved; check its unsoldered header pins first if you try again). Never connect a battery to
the 5V/3V3 pins while USB is plugged in.

## Protocol (JSON, one message per line/datagram)

Glove → server → everyone (`p` = player 1|2, `src` = "usb" | "wifi" | "sim"):

| `t` | Fields | When |
|---|---|---|
| `draw_start` | `shot`* | Fist closed (bow drawn) |
| `draw` | `shot`*, `power` 0–1, `pitch` | Every 50 ms while drawing (animate the bowstring, preview the arc; pitch as in `release`) |
| `release` | `shot`*, `power` 0–1, `held_ms`, `pitch`, `roll` (deg, null without IMU) | Hand opened → **fire the arrow** |
| `cancel` | `shot`* | Draw too weak to shoot: power under 0.3 (fist held < 0.36 s) |
| `hb` | `bend` 0–1, `state`, `shot`*, `pitch`, `roll`, `imu` | 4×/s heartbeat |
| `calib` | `open`, `fist`, `id`* | Calibration done |
| `zero` | `ok`*, `pitch0`, `roll0`, `id`* | Aim zeroed (offset now subtracted from every pitch/roll); `ok:false` = couldn't |
| `log` | `msg` | Human-readable status |

\* Arduino firmware only. Over UDP it sends `release`, `cancel`, `zero`, `calib` and `pong` 3 times; receivers
dedupe by `shot` / `id`. Commands may carry an `id` (`{"t":"cmd","cmd":"zero","id":7}`) so a retry isn't run
twice. Details: `HANDOFF.md` → "Lost packets".

Phone/page → server:
- `{"t":"cmd","p":1,"cmd":"cal"}` → recalibrates glove 1; `"zero"` → hold the aiming pose still for ~4 s and it becomes pitch 0 / roll 0 (also `"ping"`)
- `{"t":"round_start"}` → every page shows the 3-2-1 countdown (the page that sends it also sends `zero` to both gloves)
- anything else (e.g. `{"t":"hit","p":1,"target":2,"dmg":20}`, `{"t":"reset"}`) is broadcast to everyone - use this for syncing game state between the two phones.

## Game mechanics (as implemented in the mock)

- **Draw**: fist closed. Power ramps 0 → 1 over 1.2 s (`FULL_DRAW_MS`).
- **Release**: hand opens → arrow fires with that power. Power under 0.3 (`MIN_DRAW_POWER`, same as the AR app's minimum) = cancel.
- **Cooldown**: 400 ms between shots.
- **Aim**: elevation from the glove IMU pitch if present, else from the phone (in AR the phone's camera direction should set aim).
- **Damage**: 8–25 by power, ×1.5 headshot, 100 HP each.
- Power is the skill: too weak falls short, too strong overshoots.

Ideas for the AR version: shield gesture (forearm raised, IMU pitch > 60°), "charged shot" if held > 2 s, phone vibrates on `hit`.
