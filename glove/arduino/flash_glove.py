#!/usr/bin/env python3
"""Compile and upload the Arduino glove firmware (run on the laptop, glove on USB-C).

  python3 arduino/flash_glove.py --board s3 --player 1 --ssid "Bilal_iPhone" --password "..." [--ssid B --password ...]
  python3 arduino/flash_glove.py --board c3 --player 2 --ssid "<partner's hotspot>" --password "..."
  python3 arduino/flash_glove.py --board s3 --player 1 --own-network   # glove hosts "BowDuel-P1" itself
  python3 arduino/flash_glove.py --sketch ap_test --board c3           # the bare WiFi test

Real game: one glove per phone. The glove joins its phone's Personal Hotspot (--ssid/--password)
and sends its events to the phone over UDP port 9000; the AR app answers on the same port.
It also serves the test page at http://<glove IP> for browser testing.

--erase    wipe the whole flash first (needed once when the board still has MicroPython;
           also clears the saved calibration).
Needs arduino-cli with the esp32 core and the libraries listed in LIBS (see --install-libs).
"""
import argparse
import json
import glob
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
CLI = "/opt/homebrew/bin/arduino-cli" if os.path.exists("/opt/homebrew/bin/arduino-cli") else "arduino-cli"
FQBN = {   # board defaults: USB serial (hardware CDC) with "USB CDC On Boot" enabled
    "s3": "esp32:esp32:XIAO_ESP32S3",
    "c3": "esp32:esp32:XIAO_ESP32C3",
}
LIBS = ["ESP Async WebServer", "Async TCP", "ArduinoJson"]


def run(*args, **kw):
    print("+", " ".join(args), flush=True)
    return subprocess.run(args, check=True, **kw)


def espressif_ports():
    from serial.tools import list_ports
    return [p for p in list_ports.comports() if p.vid == 0x303A]


def write_config(args):
    with open(os.path.join(HERE, "glove", "config.h"), "w") as fh:
        fh.write("// Written by flash_glove.py.\n#pragma once\n")
        fh.write("#define PLAYER %d\n" % args.player)
        fh.write("#define BOARD_XIAO_%s 1\n" % args.board.upper())
        fh.write("#define WIFI_HOST %d\n" % (1 if args.own_network else 0))
        fh.write('#define WIFI_SSID "%s"\n' % args.ssid)
        fh.write("#define WIFI_CHANNEL %d\n" % (1 if args.player == 1 else 11))
        fh.write('#define WIFI_PASS "%s"\n' % ("" if args.open else args.password))
        nets = ", ".join("{%s, %s}" % (json.dumps(s), json.dumps(p)) for s, p in args.networks)
        fh.write("#define WIFI_NETWORKS {%s}   // hotspots to try (joiner)\n" % nets)
        fh.write('#define HUB_IP "192.168.4.1"\n#define UDP_PORT 9000\n')


def write_page():
    html = open(os.path.join(ROOT, "index.html"), encoding="utf-8").read()
    if ")PAGE\"" in html:
        sys.exit("index.html contains the raw-string delimiter )PAGE\"")
    with open(os.path.join(HERE, "glove", "page.h"), "w", encoding="utf-8") as fh:
        fh.write("// Generated from ../../index.html by flash_glove.py. Do not edit.\n#pragma once\n")
        fh.write('const char INDEX_HTML[] = R"PAGE(' + html + ')PAGE";\n')


def to_bootloader(port):
    """A board still running MicroPython's own USB (303a:4001) can't be reset by esptool."""
    print("Board runs MicroPython: switching it to upload mode...")
    before = {p.device for p in espressif_ports()}
    try:   # the board drops off USB mid-command, so mpremote may hang: that's expected
        subprocess.run([sys.executable, "-m", "mpremote", "connect", port, "exec",
                        "import machine; machine.bootloader()"], capture_output=True, timeout=8)
    except subprocess.TimeoutExpired:
        pass
    for _ in range(20):
        time.sleep(1)
        # the board comes back as a new port (another plugged-in board may share its USB id)
        ports = [p for p in espressif_ports() if p.pid == 0x1001 and (p.device not in before or p.device == port)]
        if ports:
            return ports[0].device
    sys.exit("The board didn't come back in upload mode. Hold BOOT, plug it in, release BOOT, run again.")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--board", choices=sorted(FQBN), required=True)
    ap.add_argument("--player", type=int, choices=[1, 2], default=1)
    ap.add_argument("--ssid", action="append", help="a phone hotspot name, exactly as the phone shows it (repeatable)")
    ap.add_argument("--password", action="append", help="its password (one per --ssid, same order)")
    ap.add_argument("--own-network", action="store_true", help="glove creates WiFi BowDuel-P<player> (password bowduel26)")
    ap.add_argument("--open", action="store_true", help="no WiFi password")
    ap.add_argument("--sketch", default="glove", choices=["glove", "ap_test"])
    ap.add_argument("--port", help="serial port (needed when several boards are plugged in)")
    ap.add_argument("--erase", action="store_true", help="wipe the whole flash first")
    ap.add_argument("--compile-only", action="store_true")
    ap.add_argument("--install-libs", action="store_true", help="install the Arduino libraries first")
    args = ap.parse_args()
    ssids, passwords = args.ssid or [], args.password or []
    if args.own_network:
        ssids = ssids or ["BowDuel-P%d" % args.player]
        passwords = passwords or ["bowduel26"]
    elif args.sketch == "glove" and not ssids:
        sys.exit("Give the phone's hotspot: --ssid NAME --password PASS (repeat both for more phones), or --own-network")
    if len(passwords) < len(ssids) and not args.open:
        sys.exit("Give one --password per --ssid")
    args.networks = list(zip(ssids, (passwords + [""] * len(ssids)) if args.open else passwords)) or [("", "")]
    args.ssid, args.password = args.networks[0]
    if args.sketch == "glove" and not args.open and len(args.password) < 8:
        sys.exit("WiFi passwords need at least 8 characters (or --open)")

    if args.install_libs:
        run(CLI, "lib", "install", *LIBS)
    if args.sketch == "glove":
        write_config(args)
        write_page()
    sketch = os.path.join(HERE, args.sketch)
    fqbn = FQBN[args.board] + (":EraseFlash=all" if args.erase else "")
    build = "/tmp/glove-build-%s-%s" % (args.sketch, args.board)
    run(CLI, "compile", "--fqbn", fqbn, "--build-path", build, sketch)
    if args.compile_only:
        return

    port = args.port
    if not port:
        ports = espressif_ports()
        if len(ports) != 1:
            sys.exit("Found %d boards: pass --port (%s)" % (len(ports), ", ".join(p.device for p in ports) or "none"))
        port = ports[0].device
    info = next((p for p in espressif_ports() if p.device == port), None)
    if info is not None and info.pid == 0x4001:
        port = to_bootloader(port)
    run(CLI, "upload", "--fqbn", fqbn, "--port", port, "--input-dir", build, sketch)
    print("\nDone: %s on %s." % (args.sketch, port))
    if args.sketch == "glove" and args.own_network:
        print("Join WiFi '%s' (%s) and open http://192.168.4.1" % (args.ssid, "no password" if args.open else "password " + args.password))
    elif args.sketch == "glove":
        print("Turn on Personal Hotspot '%s' (keep its settings screen open while the glove joins)." % args.ssid)


if __name__ == "__main__":
    main()
