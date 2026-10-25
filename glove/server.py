"""
server.py - relay between bow gloves and phones (run on a laptop).

  gloves --(USB serial now / WiFi UDP :9000 later)--> server --(WebSocket /ws)--> phones & test page

  pip install aiohttp pyserial
  python3 server.py                 # auto-finds a Pico / ESP32 on USB
  python3 server.py --no-serial     # UDP + simulator only
  python3 server.py --serial /dev/cu.usbmodem1101

Then open http://<laptop-ip>:8000 on the laptop or a phone on the same network.
Every JSON message from any glove or WebSocket client is broadcast to all WebSocket
clients. Messages {"t":"cmd","p":1,"cmd":"cal"} are sent to glove 1 instead.
"""
import argparse
import asyncio
import json
import pathlib
import socket
import threading
import time

from aiohttp import WSMsgType, web

HERE = pathlib.Path(__file__).parent
HTTP_PORT = 8000
UDP_PORT = 9000
BEACON_PORT = 9001      # server announces itself here so WiFi gloves find it without an IP

clients = set()          # WebSocket connections
udp_addrs = {}           # player -> (ip, port) of WiFi glove
serial_ports = []        # open serial.Serial objects (USB gloves)
serial_players = {}      # serial.Serial -> player number it reported
serial_lock = threading.Lock()
loop = None
udp_transport = None
QUIET = {"hb", "draw"}   # high-rate messages not printed to the console


async def broadcast(text):
    for ws in list(clients):
        try:
            await ws.send_str(text)
        except Exception:
            clients.discard(ws)


def from_glove(line, source):
    """Called with one line/datagram from a glove (any thread)."""
    line = line.strip()
    if not line:
        return
    if not line.startswith("{"):
        print(f"[{source}] {line}")
        return
    try:
        msg = json.loads(line)
    except ValueError:
        print(f"[{source}] bad json: {line}")
        return
    msg["src"] = source
    text = json.dumps(msg)
    if msg.get("t") not in QUIET:
        print(f"[{source}] {text}")
    asyncio.run_coroutine_threadsafe(broadcast(text), loop)
    return msg


def send_cmd(player, cmd):
    data = (cmd + "\n").encode()
    sent = False
    with serial_lock:
        for ser in serial_ports:
            if player is not None and serial_players.get(ser, player) != player:
                continue   # a different player's glove
            try:
                ser.write(data)
                sent = True
            except Exception:
                pass
    if player is None:
        targets = list(udp_addrs.values())
    else:
        targets = [udp_addrs[player]] if player in udp_addrs else []
    for addr in targets:
        if udp_transport:
            udp_transport.sendto(data, addr)
            sent = True
    print(f"[cmd] {cmd} -> P{player} {'sent' if sent else '(no glove connected)'}")


# ---------------- USB serial ----------------
def find_port():
    # Pico only. A XIAO ESP32-C3 glove talks over WiFi; leaving its USB port free lets
    # setup_xiao.py / mpremote use it while the server runs (use --serial to force a port).
    from serial.tools import list_ports
    for p in list_ports.comports():
        if p.vid == 0x2E8A:                    # Raspberry Pi Pico
            return p.device
    for p in list_ports.comports():
        if p.vid != 0x303A and ("usbmodem" in p.device or "ttyACM" in p.device):
            return p.device
    return None


def serial_worker(port_arg):
    import serial
    announced = False
    while True:
        port = port_arg or find_port()
        if not port:
            if not announced:
                print("[serial] no glove on USB yet (waiting...)")
                announced = True
            time.sleep(2)
            continue
        try:
            with serial.Serial(port, 115200, timeout=1) as ser:
                print(f"[serial] connected {port}")
                announced = False
                with serial_lock:
                    serial_ports.append(ser)
                try:
                    while True:
                        raw = ser.readline()
                        if raw:
                            msg = from_glove(raw.decode(errors="ignore"), "usb")
                            if msg and msg.get("p") is not None:
                                serial_players[ser] = msg["p"]
                finally:
                    with serial_lock:
                        serial_ports.remove(ser)
                        serial_players.pop(ser, None)
        except Exception as e:
            print(f"[serial] {port}: {e} (retrying)")
            time.sleep(2)


# ---------------- WiFi UDP ----------------
class GloveUdp(asyncio.DatagramProtocol):
    def connection_made(self, transport):
        global udp_transport
        udp_transport = transport

    def datagram_received(self, data, addr):
        text = data.decode(errors="ignore")
        try:
            p = json.loads(text).get("p")
            if p is not None and udp_addrs.get(p) != addr:
                udp_addrs[p] = addr
                print(f"[udp] glove P{p} at {addr[0]}")
        except ValueError:
            pass
        from_glove(text, "wifi")


def broadcast_addrs():
    """The LAN's own broadcast address (macOS often refuses 255.255.255.255), then the generic one."""
    addrs = []
    ip = lan_ip()
    try:
        import subprocess
        out = subprocess.run(["ifconfig"], capture_output=True, text=True, timeout=2).stdout
        for line in out.splitlines():
            parts = line.split()
            if len(parts) > 1 and parts[0] == "inet" and parts[1] == ip and "broadcast" in parts:
                addrs.append(parts[parts.index("broadcast") + 1])
    except Exception:
        pass
    if not addrs and ip.count(".") == 3:
        addrs.append(ip.rsplit(".", 1)[0] + ".255")   # guess a /24
    addrs.append("255.255.255.255")
    return addrs


async def beacon():
    """Broadcast 'I am the server' once a second; WiFi gloves listen for it."""
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
    s.setblocking(False)
    data = json.dumps({"t": "bowserver", "port": UDP_PORT}).encode()
    last_err, addrs, tick = None, [], 0
    while True:
        if tick % 10 == 0:              # network may change (e.g. switching to a hotspot)
            addrs = await asyncio.to_thread(broadcast_addrs)
        tick += 1
        errs = []
        for a in addrs:
            try:
                s.sendto(data, (a, BEACON_PORT))
            except OSError as e:
                errs.append(f"{a}: {e}")
        if len(errs) == len(addrs):     # nothing went out; no network yet, keep trying
            if str(errs) != last_err:
                print(f"[beacon] can't broadcast yet ({'; '.join(errs)})")
                last_err = str(errs)
        else:
            last_err = None
        await asyncio.sleep(1)


# ---------------- HTTP / WebSocket ----------------
async def ws_handler(request):
    ws = web.WebSocketResponse(heartbeat=20)
    await ws.prepare(request)
    clients.add(ws)
    print(f"[ws] client connected ({len(clients)} total) from {request.remote}")
    try:
        async for m in ws:
            if m.type != WSMsgType.TEXT:
                continue
            try:
                msg = json.loads(m.data)
            except ValueError:
                continue
            if msg.get("t") == "cmd":
                send_cmd(msg.get("p"), str(msg.get("cmd", "")))
            else:
                await broadcast(m.data)   # simulated glove events, game events (hit, reset...)
    finally:
        clients.discard(ws)
        print(f"[ws] client left ({len(clients)} total)")
    return ws


async def index(request):
    return web.FileResponse(HERE / "index.html")


def lan_ip():
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.connect(("8.8.8.8", 80))   # no packets sent; just picks the outgoing interface
        return s.getsockname()[0]
    except OSError:
        return "127.0.0.1"
    finally:
        s.close()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--serial", help="serial port of a USB glove (default: auto-detect)")
    ap.add_argument("--no-serial", action="store_true", help="don't look for USB gloves")
    args = ap.parse_args()

    async def on_startup(app):
        global loop
        loop = asyncio.get_running_loop()
        await loop.create_datagram_endpoint(GloveUdp, local_addr=("0.0.0.0", UDP_PORT))
        app["beacon"] = asyncio.create_task(beacon())
        if not args.no_serial:
            threading.Thread(target=serial_worker, args=(args.serial,), daemon=True).start()
        ip = lan_ip()
        print("=" * 60)
        print(f" Test page:      http://{ip}:{HTTP_PORT}   (or http://localhost:{HTTP_PORT})")
        print(f" WebSocket:      ws://{ip}:{HTTP_PORT}/ws")
        print(f" WiFi gloves ->  find this server automatically (beacon UDP {BEACON_PORT}),")
        print(f"                 or set SERVER_IP = \"{ip}\" in glove.py (UDP {UDP_PORT})")
        print("=" * 60)

    app = web.Application()
    app.router.add_get("/", index)
    app.router.add_get("/ws", ws_handler)
    app.on_startup.append(on_startup)
    web.run_app(app, host="0.0.0.0", port=HTTP_PORT, print=None)


if __name__ == "__main__":
    main()
