// Per-board settings. arduino/flash_glove.py writes config.h for each glove before compiling
// (config.h is gitignored because it holds the hotspot password). For the Arduino IDE instead:
// copy this file to config.h and fill it in, and run flash_glove.py --compile-only once to make page.h.
#pragma once
#define PLAYER 1
#define BOARD_XIAO_S3 1            // or BOARD_XIAO_C3
#define WIFI_HOST 0                // 0 = joins the phone's hotspot (real game), 1 = hosts its own network
#define WIFI_SSID "your-phone-hotspot"
#define WIFI_CHANNEL 1             // only used with WIFI_HOST 1
#define WIFI_PASS "hotspot-password"
#define WIFI_NETWORKS {{"your-phone-hotspot", "hotspot-password"}}   // hotspots to try, strongest wins
#define HUB_IP "192.168.4.1"
#define UDP_PORT 9000
