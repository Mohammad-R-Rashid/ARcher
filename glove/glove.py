# glove.py - bow glove firmware (MicroPython)
# Runs on a Pico (events go over USB to server.py) or a XIAO ESP32-S3/C3
# (events go over WiFi UDP to server.py).
# Copy to the board as main.py so it starts on power-up.

import time, json, sys, math
from machine import Pin, ADC, SoftI2C

# ======================= CONFIG =======================
PLAYER = 1                 # 1 or 2 - which bow this glove controls
BOARD = "pico"             # "pico", "xiao_s3" or "xiao_c3"
TRANSPORT = "serial"       # "serial" (USB to laptop) or "udp" (WiFi)

WIFI_SSID = "BowDuel"
WIFI_PASS = "bowduel26"    # at least 8 characters, or "" for an open network (no password)
WIFI_HOST = False          # True = this glove CREATES the network and is the hub: phones connect to
                           # ws://192.168.4.1/ws, the other glove sends to it, test page at http://192.168.4.1
SERVER_IP = None           # None = find server.py automatically; or its IP, e.g. "172.20.10.2"
SERVER_PORT = 9000
BEACON_PORT = 9001         # server.py announces itself here

PINS = {   # led: (pin, value that turns it on); None = board has no user LED
    "pico":    {"flex": 26, "sda": 16, "scl": 17, "led": ("LED", 1)},
    "xiao_s3": {"flex": 1,  "sda": 5,  "scl": 6,  "led": (21, 0)},     # A0, D4, D5; orange LED, on when low
    "xiao_c3": {"flex": 2,  "sda": 6,  "scl": 7,  "led": None},        # A0, D4, D5
}[BOARD]

FULL_DRAW_MS = 1200  # fist held this long = full power
MIN_DRAW_MS = 150    # shorter fists are treated as noise (cancel)
COOLDOWN_MS = 400    # min time between shots
CLOSE_AT = 0.60      # bend (0=open, 1=fist) that counts as "fist"
OPEN_AT = 0.40       # bend that counts as "open" again (hysteresis)
LOOP_MS = 20
DRAW_MSG_MS = 50     # how often to send draw progress
HB_MS = 250          # heartbeat (live sensor values) interval
CALIB_FILE = "calib.json"
TILT_ACCEL_WEIGHT = 0.04  # per sample: higher = less drift, more shake from hand motion
RELEASE_LOOKBACK_MS = 100 # shot angle is taken this long before the hand opens
# ======================================================


# ---------------- status LED ----------------
led, LED_ON = None, 1
if PINS["led"]:
    try:
        led, LED_ON = Pin(PINS["led"][0], Pin.OUT), PINS["led"][1]
    except Exception:
        led = None


def set_led(on):
    if led:
        led.value(LED_ON if on else 1 - LED_ON)


# ---------------- transport ----------------
class SerialLink:
    def __init__(self):
        import select
        self.poller = select.poll()
        self.poller.register(sys.stdin, select.POLLIN)

    def send(self, msg):
        print(json.dumps(msg))

    def recv(self):
        if self.poller.poll(0):
            return sys.stdin.readline().strip()
        return None

    def check(self):
        pass


class UdpLink:
    def __init__(self):
        import network, socket
        self.network = network
        try:
            network.hostname("glove-p%d" % PLAYER)
        except Exception:
            pass
        self.wlan = network.WLAN(network.STA_IF)
        self.wlan.active(True)
        self.join()
        if SERVER_IP:
            host, port = SERVER_IP, SERVER_PORT
        else:
            host, port = self.find_server(socket)
        print("Server at", host, port)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.setblocking(False)
        self.addr = socket.getaddrinfo(host, port)[0][-1]
        self.last_check = time.ticks_ms()

    def join(self):
        """Join WIFI_SSID, retrying forever (the network may come up after us). LED: slow blink."""
        wlan = self.wlan
        while not wlan.isconnected():
            print("Connecting to WiFi '%s'..." % WIFI_SSID)
            try:
                if WIFI_PASS:
                    wlan.connect(WIFI_SSID, WIFI_PASS)
                else:
                    wlan.connect(WIFI_SSID)
            except OSError as e:
                print("WiFi error:", e)
            for i in range(300):   # 30 s: joins have taken up to ~14 s, don't cut one off and restart it
                if wlan.isconnected():
                    break
                set_led((i // 5) % 2)
                time.sleep_ms(100)
            if not wlan.isconnected():
                print("No WiFi yet (network on? password? in range?), retrying")
                try:
                    wlan.disconnect()
                except OSError:
                    pass
        set_led(False)
        print("WiFi connected, IP", wlan.ifconfig()[0])

    def find_server(self, socket):
        """Wait for server.py's once-a-second broadcast; its sender is the server. LED: short flash."""
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.bind(("0.0.0.0", BEACON_PORT))
        s.settimeout(1)
        print("Looking for server.py on the network...")
        try:
            while True:
                if not self.wlan.isconnected():
                    self.join()
                set_led(True)
                time.sleep_ms(60)
                set_led(False)
                try:
                    data, addr = s.recvfrom(128)
                except OSError:
                    continue
                try:
                    msg = json.loads(data)
                except ValueError:
                    continue
                if msg.get("t") == "bowserver":
                    return addr[0], msg.get("port", SERVER_PORT)
        finally:
            s.close()

    def check(self):
        """Call often: rejoins the WiFi if it dropped (the glove pauses until it's back)."""
        if time.ticks_diff(time.ticks_ms(), self.last_check) < 2000:
            return
        self.last_check = time.ticks_ms()
        if not self.wlan.isconnected():
            print("WiFi lost, rejoining")
            self.join()

    def send(self, msg):
        try:
            self.sock.sendto(json.dumps(msg).encode(), self.addr)
        except OSError:
            pass

    def recv(self):
        try:
            data, _ = self.sock.recvfrom(256)
            return data.decode().strip()
        except OSError:
            return None


def start_access_point(network):
    """Create the WiFi network ourselves (like a hotspot): no router or phone needed."""
    network.WLAN(network.STA_IF).active(False)
    ap = network.WLAN(network.AP_IF)
    ap.active(False)   # after a soft reboot the AP can stay "active" without broadcasting: restart it
    time.sleep_ms(200)
    ap.active(True)
    mode = network.AUTH_WPA2_PSK if WIFI_PASS else network.AUTH_OPEN
    cfg = dict(essid=WIFI_SSID, channel=6)
    if WIFI_PASS:
        cfg["password"] = WIFI_PASS
    try:
        ap.config(security=mode, **cfg)
    except (ValueError, TypeError, AttributeError):
        ap.config(authmode=mode, **cfg)
    try:
        ap.config(max_clients=6)
    except Exception:
        pass
    print("Hosting WiFi '%s' (%s) at %s" % (WIFI_SSID, "password" if WIFI_PASS else "open", ap.ifconfig()[0]))
    return ap


class HubLink:
    """Glove 1 in the real game (no laptop): hosts the WiFi, serves the test page, relays everything.
    Phones/pages: ws://192.168.4.1/ws (same JSON messages server.py relays).
    Other glove: UDP to 192.168.4.1:SERVER_PORT; commands for it go back the same way."""
    WS_GUID = b"258EAFA5-E914-47DA-95CA-C5AB0DC85B11"

    def __init__(self):
        import network, socket
        try:
            network.hostname("glove-p%d" % PLAYER)
        except Exception:
            pass
        self.ap = start_access_point(network)
        self.stations, self.last_station_check = set(), time.ticks_ms()
        self.udp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.udp.bind(("0.0.0.0", SERVER_PORT))
        self.udp.setblocking(False)
        self.http = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.http.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.http.bind(("0.0.0.0", 80))
        self.http.listen(4)
        self.http.setblocking(False)
        self.pending = []     # [sock, request bytes, start ms]: HTTP requests still arriving
        self.clients = []     # [sock, unread bytes]: open WebSockets (phones, test pages)
        self.gloves = {}      # player -> UDP address of the other glove(s)
        self.cmds = []        # commands for this glove, from phones
        print("Hub ready: test page http://192.168.4.1  websocket ws://192.168.4.1/ws")

    # ---- what the rest of glove.py uses ----
    def send(self, msg):
        msg["src"] = "wifi"
        self.broadcast(json.dumps(msg))

    def recv(self):
        self.service()
        return self.cmds.pop(0) if self.cmds else None

    def check(self):
        self.service()

    def note(self, text):
        """Hub status goes to the pages' log panel. Not print(): with the board on a computer's USB
        and nothing reading it, print() blocks once its buffer fills and the whole glove freezes."""
        self.broadcast(json.dumps({"t": "log", "p": PLAYER, "msg": "hub: " + text, "src": "wifi"}))

    # ---- network plumbing ----
    def service(self):
        if time.ticks_diff(time.ticks_ms(), self.last_station_check) > 1000:   # log WiFi joins/leaves on USB
            self.last_station_check = time.ticks_ms()
            try:
                now = set(":".join("%02x" % b for b in st[0]) for st in self.ap.status("stations"))
            except Exception:
                now = self.stations
            for mac in now - self.stations:
                self.note("WiFi device joined: %s (%d on BowDuel)" % (mac, len(now)))
            for mac in self.stations - now:
                self.note("WiFi device left: %s (%d on BowDuel)" % (mac, len(now)))
            self.stations = now
        try:
            conn, _ = self.http.accept()
            conn.setblocking(False)
            self.pending.append([conn, b"", time.ticks_ms()])
        except OSError:
            pass
        for p in self.pending[:]:
            try:
                data = p[0].recv(1024)
            except OSError:
                data = None                      # nothing yet
            if data == b"" or time.ticks_diff(time.ticks_ms(), p[2]) > 3000 or len(p[1]) > 4096:
                self.pending.remove(p)
                p[0].close()
                continue
            if data:
                p[1] += data
            if b"\r\n\r\n" in p[1]:
                self.pending.remove(p)
                self.handle_http(p[0], p[1])
        for c in self.clients[:]:
            try:
                data = c[0].recv(512)
            except OSError:
                continue
            if not data:
                self.drop(c)
                continue
            c[1] += data
            self.read_frames(c)
        while True:
            try:
                data, addr = self.udp.recvfrom(512)
            except OSError:
                break
            try:
                msg = json.loads(data)
            except ValueError:
                continue
            p = msg.get("p")
            if p is not None and self.gloves.get(p) != addr:
                self.gloves[p] = addr
                self.note("glove P%s connected from %s" % (p, addr[0]))
            msg["src"] = "wifi"
            self.broadcast(json.dumps(msg))

    def handle_http(self, conn, req):
        try:
            lines = req.split(b"\r\n\r\n")[0].decode().split("\r\n")
            parts = lines[0].split(" ")
            path = parts[1] if len(parts) > 1 else "/"
            headers = {}
            for line in lines[1:]:
                if ":" in line:
                    k, v = line.split(":", 1)
                    headers[k.strip().lower()] = v.strip()
            if headers.get("upgrade", "").lower() == "websocket":
                import hashlib, binascii
                key = headers.get("sec-websocket-key", "").encode()
                accept = binascii.b2a_base64(hashlib.sha1(key + self.WS_GUID).digest()).strip()
                self.write(conn, b"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\n"
                                 b"Connection: Upgrade\r\nSec-WebSocket-Accept: " + accept + b"\r\n\r\n")
                self.clients.append([conn, b""])
                self.note("page/phone connected (%d open)" % len(self.clients))
            elif path in ("/", "/index.html"):
                self.send_file(conn, "index.html")
            else:
                self.write(conn, b"HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n")
                conn.close()
        except Exception as e:
            self.note("http error: %s" % e)
            conn.close()

    def send_file(self, conn, name):
        import os
        try:
            size = os.stat(name)[6]
        except OSError:
            self.write(conn, b"HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n")
            conn.close()
            return
        self.write(conn, ("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n"
                          "Content-Length: %d\r\nConnection: close\r\n\r\n" % size).encode())
        with open(name, "rb") as fh:
            while True:
                chunk = fh.read(1024)
                if not chunk:
                    break
                self.write(conn, chunk)
        conn.close()

    def write(self, conn, data):
        conn.settimeout(0.5)                     # a stalled phone gets dropped, not waited on
        try:
            conn.sendall(data)
        finally:
            conn.setblocking(False)

    def read_frames(self, c):
        buf = c[1]
        while len(buf) >= 2:
            op, n, i = buf[0] & 0x0F, buf[1] & 0x7F, 2
            masked = buf[1] & 0x80
            if n == 126:
                if len(buf) < 4:
                    break
                n, i = (buf[2] << 8) | buf[3], 4
            elif n == 127:                       # we never expect huge frames
                self.drop(c)
                return
            if masked:
                if len(buf) < i + 4:
                    break
                mask = buf[i:i + 4]
                i += 4
            if len(buf) < i + n:
                break
            payload = bytearray(buf[i:i + n])
            if masked:
                for k in range(n):
                    payload[k] ^= mask[k & 3]
            buf = buf[i + n:]
            if op == 1:
                self.from_phone(bytes(payload))
            elif op == 8:
                self.drop(c)
                return
            elif op == 9:                        # ping -> pong
                try:
                    self.write(c[0], self.frame(bytes(payload), 0xA))
                except OSError:
                    self.drop(c)
                    return
        c[1] = buf

    def from_phone(self, text):
        try:
            msg = json.loads(text)
        except ValueError:
            return
        if msg.get("t") != "cmd":
            self.broadcast(text)                 # game events (hit, reset, round_start...) go to everyone
            return
        p, cmd = msg.get("p"), str(msg.get("cmd", ""))
        if p in (None, PLAYER):
            self.cmds.append(cmd)
        if p == PLAYER:
            return
        targets = [self.gloves[p]] if p in self.gloves else (list(self.gloves.values()) if p is None else [])
        for addr in targets:
            self.udp.sendto((cmd + "\n").encode(), addr)

    @staticmethod
    def frame(data, op=1):
        n = len(data)
        head = bytes((0x80 | op, n)) if n < 126 else bytes((0x80 | op, 126, n >> 8, n & 255))
        return head + data

    def broadcast(self, text):
        f = self.frame(text if isinstance(text, bytes) else text.encode())
        for c in self.clients[:]:
            try:
                self.write(c[0], f)
            except OSError:
                self.drop(c)

    def drop(self, c):
        if c in self.clients:
            self.clients.remove(c)
        try:
            c[0].close()
        except OSError:
            pass


link = SerialLink() if TRANSPORT == "serial" else HubLink() if WIFI_HOST else UdpLink()


def send(t, **kw):
    kw["t"] = t
    kw["p"] = PLAYER
    link.send(kw)


def log(text):
    send("log", msg=text)


# ---------------- flex sensor ----------------
flex_adc = ADC(Pin(PINS["flex"]))
# ESP32: full 0-3.3 V range (the constant was renamed in newer ESP-IDF); the Pico has neither
_atten = getattr(ADC, "ATTN_11DB", None) or getattr(ADC, "ATTN_12DB", None)
if _atten is not None:
    flex_adc.atten(_atten)


def flex_raw(n=8):
    s = 0
    for _ in range(n):
        s += flex_adc.read_u16()
    return s / n


calib = {"open": None, "fist": None, "pitch0": 0.0, "roll0": 0.0}


def bend():
    """0.0 = hand open, 1.0 = full fist (works whichever way voltage moves)."""
    o, f = calib["open"], calib["fist"]
    b = (flex_raw() - o) / (f - o)
    return 0.0 if b < 0 else 1.0 if b > 1 else b


busy = {"state": None, "hb": 0}


def busy_wait(ms):
    """Sleep, but keep sending heartbeats so the page shows 'busy', not 'offline'."""
    end = time.ticks_add(time.ticks_ms(), ms)
    while True:
        now = time.ticks_ms()
        if time.ticks_diff(now, busy["hb"]) >= HB_MS:
            pitch, roll = tilt()
            send("hb", bend=None, state=busy["state"], pitch=pitch, roll=roll, imu=imu is not None)
            busy["hb"] = now
        link.check()
        left = time.ticks_diff(end, now)
        if left <= 0:
            return
        time.sleep_ms(min(20, left))


def _median_over(ms):
    vals = []
    end = time.ticks_add(time.ticks_ms(), ms)
    while time.ticks_diff(end, time.ticks_ms()) > 0:
        vals.append(flex_raw())
        busy_wait(20)
    vals.sort()
    return vals[len(vals) // 2]


def calibrate():
    busy["state"] = "calibrating"
    while True:
        log("CALIBRATE: hold hand OPEN (LED on)")
        set_led(True)
        busy_wait(2000)
        o = _median_over(1000)
        log("CALIBRATE: make a FIST (LED blinking)")
        for i in range(10):
            set_led(i % 2)
            busy_wait(200)
        f = _median_over(1000)
        set_led(False)
        if abs(f - o) > 1500:   # ~75 mV of change minimum
            calib["open"], calib["fist"] = o, f
            save_calib()
            send("calib", open=int(o), fist=int(f))
            return
        log("Calibration failed: open and fist read the same (%d vs %d). Check flex wiring. Retrying..." % (o, f))


def save_calib():
    try:
        with open(CALIB_FILE, "w") as fh:
            json.dump(calib, fh)
    except OSError:
        pass


def load_calib():
    try:
        with open(CALIB_FILE) as fh:
            c = json.load(fh)
        if abs(c["fist"] - c["open"]) > 1500:
            calib.update(c)
            return True
    except (OSError, ValueError, KeyError):
        pass
    return False


# ---------------- IMU (optional) ----------------
imu = None
try:
    from lsm6dso32 import LSM6DSO32
    imu = LSM6DSO32(SoftI2C(sda=Pin(PINS["sda"]), scl=Pin(PINS["scl"]), freq=100_000))
except Exception as e:
    imu = None
    _imu_err = str(e)


def _accel_tilt(ax, ay, az):
    pitch = math.atan2(-ax, math.sqrt(ay * ay + az * az)) * 57.29578
    roll = math.atan2(ay, az) * 57.29578
    return pitch, roll


# Complementary filter: gyro for fast motion, gravity (accelerometer) to stop drift.
att = {"pitch": None, "roll": None, "t": 0}
tilt_hist = []   # (ticks_ms, pitch, roll) for the last ~300 ms, zero offset applied


def calibrate_gyro(samples=200):
    if imu is None:
        return
    try:
        imu.calibrate_gyro(samples)
    except OSError:
        pass


def update_tilt():
    """Read the IMU once and advance the filter. Call every loop."""
    if imu is None:
        return
    try:
        ax, ay, az = imu.acceleration
        gx, gy, gz = imu.gyro_dps
    except OSError:
        return  # flaky I2C: skip this sample, keep the last angle
    now = time.ticks_us()
    ap, ar = _accel_tilt(ax, ay, az)
    if att["pitch"] is None:
        att["pitch"], att["roll"] = ap, ar
    else:
        dt = time.ticks_diff(now, att["t"]) / 1_000_000
        if dt > 0.2:
            dt = 0.2
        p, r = att["pitch"] / 57.29578, att["roll"] / 57.29578
        cp = math.cos(p)
        if abs(cp) < 0.05:
            cp = 0.05 if cp >= 0 else -0.05
        # gyro rates (body frame) -> pitch/roll rates
        roll_rate = gx + (gy * math.sin(r) + gz * math.cos(r)) * math.sin(p) / cp
        pitch_rate = gy * math.cos(r) - gz * math.sin(r)
        gp = att["pitch"] + pitch_rate * dt
        gr = att["roll"] + roll_rate * dt
        # trust gravity less while the hand is accelerating
        g = math.sqrt(ax * ax + ay * ay + az * az) / 9.80665
        k = TILT_ACCEL_WEIGHT if abs(g - 1) < 0.15 else 0.0
        if ar - gr > 180:
            gr += 360
        elif gr - ar > 180:
            gr -= 360
        att["pitch"] = gp + k * (ap - gp)
        r = gr + k * (ar - gr)
        att["roll"] = r - 360 if r > 180 else r + 360 if r < -180 else r
    att["t"] = now
    ms = time.ticks_ms()
    tilt_hist.append((ms, att["pitch"] - calib["pitch0"], att["roll"] - calib["roll0"]))
    while len(tilt_hist) > 2 and time.ticks_diff(ms, tilt_hist[0][0]) > 300:
        tilt_hist.pop(0)


def tilt(ago_ms=0):
    """Zeroed pitch/roll in degrees from about ago_ms ago, or (None, None)."""
    if not tilt_hist:
        return None, None
    now = time.ticks_ms()
    pick = tilt_hist[-1]
    for h in reversed(tilt_hist):
        pick = h
        if time.ticks_diff(now, h[0]) >= ago_ms:
            break
    return round(pick[1], 1), round(pick[2], 1)


def zero_tilt():
    """Hold the aiming pose still: that pose becomes pitch 0, roll 0."""
    if imu is None:
        log("Zero aim: no IMU")
        return
    log("ZERO AIM: hold your aiming pose still (LED on)")
    busy["state"] = "zeroing"
    set_led(True)
    busy_wait(1500)
    calibrate_gyro(100)       # glove is still, so re-measure gyro drift too (~0.6 s)
    att["pitch"] = None       # restart the filter from gravity
    ps, rs, n = 0.0, 0.0, 0
    end = time.ticks_add(time.ticks_ms(), 1000)
    while time.ticks_diff(end, time.ticks_ms()) > 0:
        update_tilt()
        if att["pitch"] is not None:
            ps += att["pitch"]
            rs += att["roll"]
            n += 1
        busy_wait(LOOP_MS)
    set_led(False)
    if n == 0:
        log("Zero aim failed: IMU not responding")
        return
    calib["pitch0"], calib["roll0"] = round(ps / n, 2), round(rs / n, 2)
    tilt_hist.clear()
    save_calib()
    send("zero", pitch0=calib["pitch0"], roll0=calib["roll0"])
    log("Aim zeroed (offset pitch %.1f, roll %.1f)" % (calib["pitch0"], calib["roll0"]))


# ---------------- commands from server ----------------
def handle_cmd(cmd):
    if cmd == "cal":
        calibrate()
    elif cmd == "zero":
        zero_tilt()
    elif cmd == "ping":
        send("pong")
    if cmd in ("cal", "zero"):
        while link.recv():
            pass   # drop button presses that queued up while we were busy


# ---------------- main loop ----------------
def main():
    log("glove P%d booting (%s, %s)" % (PLAYER, BOARD, TRANSPORT))
    log("IMU ok" if imu else "IMU not found - continuing with flex only (%s)" % _imu_err)
    if imu:
        log("Measuring gyro drift: keep the glove still for 1 s")
        calibrate_gyro()
    if load_calib():
        send("calib", open=int(calib["open"]), fist=int(calib["fist"]))
    else:
        calibrate()

    state = "open"
    draw_start = 0
    last_release = time.ticks_add(time.ticks_ms(), -COOLDOWN_MS)
    last_draw_msg = 0
    last_hb = 0

    while True:
        update_tilt()
        now = time.ticks_ms()
        b = bend()

        if state == "open":
            if b >= CLOSE_AT and time.ticks_diff(now, last_release) >= COOLDOWN_MS:
                state = "drawing"
                draw_start = now
                last_draw_msg = now
                send("draw_start")
        else:
            held = time.ticks_diff(now, draw_start)
            power = min(1.0, held / FULL_DRAW_MS)
            if b <= OPEN_AT:
                state = "open"
                if held >= MIN_DRAW_MS:
                    pitch, roll = tilt(RELEASE_LOOKBACK_MS)
                    send("release", power=round(power, 2), held_ms=held, pitch=pitch, roll=roll)
                    last_release = now
                else:
                    send("cancel")
            elif time.ticks_diff(now, last_draw_msg) >= DRAW_MSG_MS:
                # same lookback as release, so the app's arc preview shows exactly the shot it would get
                pitch, _ = tilt(RELEASE_LOOKBACK_MS)
                send("draw", power=round(power, 2), pitch=pitch)
                last_draw_msg = now

        if time.ticks_diff(now, last_hb) >= HB_MS:
            pitch, roll = tilt()
            send("hb", bend=round(b, 2), state=state, pitch=pitch, roll=roll, imu=imu is not None)
            last_hb = now

        cmd = link.recv()
        if cmd:
            handle_cmd(cmd)
        link.check()

        time.sleep_ms(LOOP_MS)


main()
