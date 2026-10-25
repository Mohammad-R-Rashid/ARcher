#!/usr/bin/env python3
"""One-command setup for a XIAO ESP32-S3 (or -C3) glove (run on the laptop, board plugged in by USB-C).

  python3 setup_xiao.py --player 1 --host                  # glove 1 CREATES WiFi "BowDuel"
  python3 setup_xiao.py --player 2 --board xiao_c3         # glove 2 joins it (C3 board)
  python3 setup_xiao.py --player 1 --ssid "My Hotspot" --password "pass"   # join some other network

Phones (and a laptop, for testing) join "BowDuel" (password bowduel26): the AR app connects to
ws://192.168.4.1/ws, and http://192.168.4.1 opens the test page. No laptop or server.py needed.

--flash   erase the board and install MicroPython first (a new board, or a broken one).
          Leave it off to just re-upload the glove code (keeps the saved calibration).

Then the glove reboots, creates/joins the network, finds server.py by itself, and (first time only)
runs the flex calibration: follow the prompts on the test page (the S3's orange LED also shows them).
"""
import argparse
import glob
import os
import re
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ESPRESSIF_VID = 0x303A
BOARDS = {   # board -> (esptool chip, firmware file pattern in firmware/)
    "xiao_s3": ("esp32s3", "ESP32_GENERIC_S3-SPIRAM_OCT-*.bin"),   # XIAO S3 has octal PSRAM
    "xiao_c3": ("esp32c3", "ESP32_GENERIC_C3-*.bin"),
}


def run(*args):
    print("+", " ".join(args))
    subprocess.run(args, check=True)


def find_port():
    from serial.tools import list_ports
    for p in list_ports.comports():
        if p.vid == ESPRESSIF_VID:
            return p.device
    return None


def port_in_use(port):
    out = subprocess.run(["lsof", port], capture_output=True, text=True).stdout
    return out.strip()


def glove_source(board, player, ssid, password, host=False):
    """glove.py with the config block set for a WiFi XIAO glove."""
    src = open(os.path.join(HERE, "glove.py")).read()
    settings = {
        "PLAYER": str(player),
        "BOARD": '"%s"' % board,
        "TRANSPORT": '"udp"',
        "WIFI_SSID": repr(ssid),
        "WIFI_PASS": repr(password),
        "WIFI_HOST": str(bool(host)),
    }
    if not host and ssid == "BowDuel":
        settings["SERVER_IP"] = '"192.168.4.1"'   # the host glove's fixed address
    for name, value in settings.items():
        src, n = re.subn(r"^%s = .*?(\s+#.*)?$" % name,
                         lambda m: "%s = %s%s" % (name, value, m.group(1) or ""),
                         src, count=1, flags=re.M)
        if n != 1:
            sys.exit("could not find %s in glove.py's config block" % name)
    return src


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--player", type=int, choices=[1, 2], required=True)
    ap.add_argument("--board", choices=sorted(BOARDS), default="xiao_s3")
    ap.add_argument("--host", action="store_true", help="this glove creates the WiFi network (only one glove)")
    ap.add_argument("--ssid", default="BowDuel", help="network name (default BowDuel)")
    ap.add_argument("--password", default="bowduel26", help="at least 8 characters (default bowduel26)")
    ap.add_argument("--flash", action="store_true", help="erase and install MicroPython first")
    ap.add_argument("--port", help="serial port (default: the first Espressif board found)")
    args = ap.parse_args()
    if len(args.password) < 8:
        sys.exit("WiFi passwords must be at least 8 characters")

    port = args.port or find_port()
    if not port:
        sys.exit("No XIAO found on USB. Plug it in with a data USB-C cable. If it still doesn't show up, "
                 "hold BOOT, plug it in, release BOOT, and run again.")
    busy = port_in_use(port)
    if busy:
        sys.exit("%s is in use (server.py or mpremote?). Stop it first, or start server.py with --no-serial.\n%s"
                 % (port, busy))
    print("XIAO on", port)

    if args.flash:
        chip, pattern = BOARDS[args.board]
        fw = sorted(glob.glob(os.path.join(HERE, "firmware", pattern)))
        if not fw:
            sys.exit("No %s in firmware/. Download it from https://micropython.org/download/" % pattern)
        esptool = [sys.executable, "-m", "esptool", "--chip", chip, "--port", port]
        run(*esptool, "erase_flash")
        run(*esptool, "--baud", "460800", "write_flash", "-z", "0x0", fw[-1])
        print("waiting for MicroPython to boot...")
        for _ in range(20):
            time.sleep(1)
            port = args.port or find_port() or port
            if os.path.exists(port):
                break
        time.sleep(2)

    with tempfile.TemporaryDirectory() as tmp:
        main_py = os.path.join(tmp, "main.py")
        with open(main_py, "w") as fh:
            fh.write(glove_source(args.board, args.player, args.ssid, args.password, args.host))
        mp = [sys.executable, "-m", "mpremote", "connect", port]
        files = ["cp", os.path.join(HERE, "lsm6dso32.py"), ":lsm6dso32.py", "+", "cp", main_py, ":main.py"]
        if args.host:   # the hub serves the test page itself
            files += ["+", "cp", os.path.join(HERE, "index.html"), ":index.html"]
        run(*mp, *files, "+", "reset")

    action = "creates WiFi" if args.host else "joins"
    print("\nDone. Glove P%d is rebooting: it %s '%s', then finds server.py on its own." % (args.player, action, args.ssid))
    if args.host:
        print("Join '%s' (password %s) on a phone or laptop and open http://192.168.4.1" % (args.ssid, args.password))
    print("Open the test page: the P%d card turns 'online (wifi)'." % args.player)
    print("First boot only: flex calibration starts automatically. The page shows STRAIGHT / BEND prompts.")
    print("To watch the board's own output: python3 -m mpremote connect %s" % port)


if __name__ == "__main__":
    main()
