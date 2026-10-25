// glove.ino - bow glove firmware (Arduino, Seeed XIAO ESP32-S3 / ESP32-C3)
// Arduino port of ../../glove.py: same JSON messages, calibration and draw/release logic.
// Per-board settings (player, board, WiFi) are in config.h, written by ../flash_glove.py.
//
// Real game (WIFI_HOST 0): one glove per phone. The phone runs Personal Hotspot, the glove joins it
//   and sends every event as a JSON datagram to the phone (the WiFi's router) on UDP port 9000.
//   The phone's AR app sends commands ("zero", "cal" or {"t":"cmd",...}) back to the glove's port 9000.
// Testing: the glove also serves the test page at http://<glove IP> (WebSocket /ws), so a browser
//   can watch it; http://<glove 1 IP>/?gloves=<glove 2 IP> shows both gloves on one page.
// WIFI_HOST 1: the glove creates its own WiFi network instead (192.168.4.1), for tests with no hotspot.

#include <Arduino.h>
#include <WiFi.h>
#include <WiFiUdp.h>
#include <ESPmDNS.h>
#include <Wire.h>
#include <Preferences.h>
#include <AsyncTCP.h>
#include <ESPAsyncWebServer.h>
#include <ArduinoJson.h>
#include <algorithm>
#include <stdarg.h>
#include "config.h"
#include "page.h"

// ======================= board pins =======================
#if defined(BOARD_XIAO_S3)
const char* BOARD_NAME = "xiao_s3";
const int PIN_FLEX = 1, PIN_SDA = 5, PIN_SCL = 6;    // A0, D4, D5
const int PIN_LED = 21, LED_ON = LOW;                 // orange user LED, on when low
#elif defined(BOARD_XIAO_C3)
const char* BOARD_NAME = "xiao_c3";
const int PIN_FLEX = 2, PIN_SDA = 6, PIN_SCL = 7;    // A0, D4, D5
const int PIN_LED = -1, LED_ON = LOW;                 // no user LED
#else
#error "config.h must define BOARD_XIAO_S3 or BOARD_XIAO_C3"
#endif

// ======================= tuning (same as glove.py) =======================
const uint32_t FULL_DRAW_MS = 1200;   // fist held this long = full power
const uint32_t MIN_DRAW_MS = 150;     // shorter fists are treated as noise (cancel)
const uint32_t COOLDOWN_MS = 400;     // min time between shots
const float CLOSE_AT = 0.60f;         // bend (0=open, 1=fist) that counts as "fist"
const float OPEN_AT = 0.40f;          // bend that counts as "open" again (hysteresis)
const uint32_t LOOP_MS = 20;
const uint32_t DRAW_MSG_MS = 50;      // how often to send draw progress
const uint32_t HB_MS = 250;           // heartbeat (live sensor values) interval
const float TILT_ACCEL_WEIGHT = 0.04f;        // per sample: higher = less drift, more shake
const uint32_t RELEASE_LOOKBACK_MS = 100;     // shot angle is taken this long before the hand opens
const float MIN_CAL_SPAN_MV = 75.0f;          // open and fist must differ by this much

// ---------------- status LED ----------------
void setLed(bool on) {
  if (PIN_LED >= 0) digitalWrite(PIN_LED, on ? LED_ON : !LED_ON);
}

// ---------------- commands for this glove ----------------
String cmdQueue[8];
int cmdHead = 0, cmdCount = 0;
void pushCmd(const String& c) {
  if (cmdCount < 8) cmdQueue[(cmdHead + cmdCount++) % 8] = c;
}
bool popCmd(String& c) {
  if (!cmdCount) return false;
  c = cmdQueue[cmdHead];
  cmdHead = (cmdHead + 1) % 8;
  cmdCount--;
  return true;
}

// ======================= network =======================
AsyncWebServer server(80);
AsyncWebSocket ws("/ws");             // test pages
struct NetMsg { char text[256]; };
QueueHandle_t pageQueue;              // messages from test pages, filled by the web server's task
WiFiUDP udp;
IPAddress phoneIp;                    // WIFI_HOST 0: where events go = the hotspot phone (the WiFi's router)
IPAddress gloveIp[3];
uint16_t glovePort[3] = {0, 0, 0};    // WIFI_HOST 1: gloves that joined this glove's network
int lastStations = -1;
size_t lastPages = 0;

void linkSend(const char* json) {
  ws.textAll(json);
#if !WIFI_HOST
  if (WiFi.status() == WL_CONNECTED) {
    udp.beginPacket(phoneIp, UDP_PORT);
    udp.write((const uint8_t*)json, strlen(json));
    udp.endPacket();
  }
#endif
}

void note(const char* text) {         // status for the page's log panel (and the phone app)
  char buf[220];
  snprintf(buf, sizeof buf, "{\"t\":\"log\",\"p\":%d,\"msg\":\"%s\",\"src\":\"wifi\"}", PLAYER, text);
  linkSend(buf);
  Serial.println(text);
}

void onWsEvent(AsyncWebSocket*, AsyncWebSocketClient*, AwsEventType type, void* arg, uint8_t* data, size_t len) {
  if (type != WS_EVT_DATA) return;
  AwsFrameInfo* info = (AwsFrameInfo*)arg;
  if (info->final && info->index == 0 && info->len == len && info->opcode == WS_TEXT && len < sizeof(NetMsg::text)) {
    NetMsg m;
    memcpy(m.text, data, len);
    m.text[len] = 0;
    xQueueSend(pageQueue, &m, 0);     // handled in loop(), not in the web server's task
  }
}

// A JSON message from a page or the phone app. Commands for this glove are queued; commands for a
// glove that joined our own network are passed to it; anything else (hit, round_start...) goes to the pages.
void handleJson(const char* text) {
  JsonDocument doc;
  if (deserializeJson(doc, text)) return;
  if (strcmp(doc["t"] | "", "cmd") != 0) {
    ws.textAll(text);
    return;
  }
  const char* cmd = doc["cmd"] | "";
  bool anyone = doc["p"].isNull();
  int p = doc["p"] | 0;
  if (anyone || p == PLAYER) pushCmd(cmd);
  for (int i = 1; i <= 2; i++) {
    if (i == PLAYER || !glovePort[i] || (!anyone && i != p)) continue;
    udp.beginPacket(gloveIp[i], glovePort[i]);
    udp.printf("%s\n", cmd);
    udp.endPacket();
  }
}

void startWeb() {
  pageQueue = xQueueCreate(16, sizeof(NetMsg));
  ws.onEvent(onWsEvent);
  server.addHandler(&ws);
  server.on("/", HTTP_GET, [](AsyncWebServerRequest* r) {
    r->send(200, "text/html", (const uint8_t*)INDEX_HTML, sizeof(INDEX_HTML) - 1);
  });
  server.onNotFound([](AsyncWebServerRequest* r) { r->send(404); });
  server.begin();
  udp.begin(UDP_PORT);
}

void linkBegin() {
#if WIFI_HOST
  WiFi.mode(WIFI_AP);
  WiFi.softAPConfig(IPAddress(192, 168, 4, 1), IPAddress(192, 168, 4, 1), IPAddress(255, 255, 255, 0));
  bool ok = WiFi.softAP(WIFI_SSID, strlen(WIFI_PASS) ? WIFI_PASS : nullptr, WIFI_CHANNEL, 0, 4);
  Serial.printf("Hosting WiFi '%s' at 192.168.4.1: %s\n", WIFI_SSID, ok ? "ok" : "FAILED");
#else
  WiFi.mode(WIFI_STA);
  WiFi.setHostname(PLAYER == 1 ? "glove-p1" : "glove-p2");
  WiFi.setAutoReconnect(true);        // rejoins by itself if the hotspot drops
  while (true) {                      // the hotspot may come up after us: keep trying
    Serial.printf("Connecting to WiFi '%s'...\n", WIFI_SSID);
    if (strlen(WIFI_PASS)) WiFi.begin(WIFI_SSID, WIFI_PASS);
    else WiFi.begin(WIFI_SSID);
    uint32_t t0 = millis();
    while (WiFi.status() != WL_CONNECTED && millis() - t0 < 30000) {
      setLed((millis() / 500) % 2);   // slow blink = looking for the phone's hotspot
      delay(100);
    }
    if (WiFi.status() == WL_CONNECTED) break;
    Serial.printf("No WiFi yet (status %d). Scanning...\n", (int)WiFi.status());
    WiFi.disconnect();                // a scan fails while a join attempt is still running
    delay(300);
    int n = WiFi.scanNetworks();
    bool seen = false;
    for (int i = 0; i < n; i++) {
      bool target = WiFi.SSID(i) == WIFI_SSID;
      seen |= target;
      if (target || i < 6)            // results come strongest first
        Serial.printf("  %s'%s' %d dBm, channel %d, security %d\n", target ? ">> " : "", WiFi.SSID(i).c_str(),
                      (int)WiFi.RSSI(i), (int)WiFi.channel(i), (int)WiFi.encryptionType(i));
    }
    if (!seen) Serial.printf("  '%s' NOT among %d networks (hotspot on? Personal Hotspot screen open? Maximize Compatibility?)\n", WIFI_SSID, n);
    WiFi.scanDelete();
    delay(200);
  }
  setLed(false);
  WiFi.setSleep(false);               // lower latency
  phoneIp = WiFi.gatewayIP();
  MDNS.begin(PLAYER == 1 ? "glove-p1" : "glove-p2");
  MDNS.addService("http", "tcp", 80);
  Serial.printf("WiFi connected: glove IP %s, sending to phone %s port %d\n",
                WiFi.localIP().toString().c_str(), phoneIp.toString().c_str(), UDP_PORT);
#endif
  startWeb();
}

void linkService() {
  NetMsg m;
  while (xQueueReceive(pageQueue, &m, 0) == pdTRUE) handleJson(m.text);
  while (udp.parsePacket() > 0) {
    char buf[320];
    int len = udp.read(buf, sizeof buf - 1);
    if (len <= 0) continue;
    buf[len] = 0;
    String text(buf);
    text.trim();
    if (!text.length()) continue;
    if (text[0] != '{') {             // a plain command from the phone app, e.g. "zero"
      pushCmd(text);
    } else if (text.indexOf("\"cmd\"") >= 0) {   // {"t":"cmd","p":..,"cmd":..} from the phone app
      handleJson(text.c_str());
    } else {                          // an event from a glove that joined our network: show it
      int at = text.indexOf("\"p\":");
      int p = at >= 0 ? text.substring(at + 4).toInt() : 0;
      if (p >= 1 && p <= 2 && (gloveIp[p] != udp.remoteIP() || glovePort[p] != udp.remotePort())) {
        gloveIp[p] = udp.remoteIP();
        glovePort[p] = udp.remotePort();
        char t[64];
        snprintf(t, sizeof t, "glove P%d connected from %s", p, gloveIp[p].toString().c_str());
        note(t);
      }
      ws.textAll(text.c_str());
    }
  }
  static uint32_t lastHousekeeping = 0;
  if (millis() - lastHousekeeping > 1000) {
    lastHousekeeping = millis();
    ws.cleanupClients();
#if !WIFI_HOST
    if (WiFi.status() == WL_CONNECTED) phoneIp = WiFi.gatewayIP();
    int st = WiFi.status() == WL_CONNECTED ? 1 : 0;
#else
    int st = WiFi.softAPgetStationNum();
#endif
    size_t pages = ws.count();
    if (st != lastStations || pages != lastPages) {
      char t[100];
#if WIFI_HOST
      snprintf(t, sizeof t, "%d WiFi device(s) on %s, %u test page(s) open", st, WIFI_SSID, (unsigned)pages);
#else
      snprintf(t, sizeof t, "%s, %u test page(s) open", st ? "on the hotspot" : "hotspot lost, rejoining", (unsigned)pages);
#endif
      note(t);
      lastStations = st;
      lastPages = pages;
    }
  }
}

// ---------------- messages ----------------
void emit(const char* type, const char* fmt = nullptr, ...) {
  char extra[240] = "";
  if (fmt) {
    va_list ap;
    va_start(ap, fmt);
    vsnprintf(extra, sizeof extra, fmt, ap);
    va_end(ap);
  }
  char msg[320];
  snprintf(msg, sizeof msg, "{\"t\":\"%s\",\"p\":%d,\"src\":\"wifi\"%s%s}", type, PLAYER, extra[0] ? "," : "", extra);
  linkSend(msg);
}

void logMsg(const char* text) {
  emit("log", "\"msg\":\"%s\"", text);
  Serial.println(text);
}

const char* angle(char* buf, bool ok, float v) {   // degrees with 1 decimal, or null
  if (ok) snprintf(buf, 12, "%.1f", v);
  else strcpy(buf, "null");
  return buf;
}

// ---------------- flex sensor ----------------
float flexMv() {
  uint32_t s = 0;
  for (int i = 0; i < 8; i++) s += analogReadMilliVolts(PIN_FLEX);
  return s / 8.0f;
}

struct { float open, fist, pitch0, roll0; bool valid; } calib = {0, 0, 0, 0, false};
Preferences prefs;

void saveCalib() {
  prefs.begin("glove", false);
  prefs.putFloat("open", calib.open);
  prefs.putFloat("fist", calib.fist);
  prefs.putFloat("pitch0", calib.pitch0);
  prefs.putFloat("roll0", calib.roll0);
  prefs.putBool("valid", calib.valid);
  prefs.end();
}

bool loadCalib() {
  if (!prefs.begin("glove", true)) return false;   // nothing saved yet
  calib.open = prefs.getFloat("open", 0);
  calib.fist = prefs.getFloat("fist", 0);
  calib.pitch0 = prefs.getFloat("pitch0", 0);
  calib.roll0 = prefs.getFloat("roll0", 0);
  calib.valid = prefs.getBool("valid", false) && fabsf(calib.fist - calib.open) > MIN_CAL_SPAN_MV;
  prefs.end();
  return calib.valid;
}

float bend() {   // 0.0 = hand open, 1.0 = full fist (works whichever way the voltage moves)
  float b = (flexMv() - calib.open) / (calib.fist - calib.open);
  return b < 0 ? 0 : b > 1 ? 1 : b;
}

// ---------------- IMU (LSM6DSO32, optional) ----------------
uint8_t imuAddr = 0;
bool imuOk = false;
float gyroBias[3] = {0, 0, 0};   // deg/s

bool imuWrite(uint8_t reg, uint8_t val) {
  Wire.beginTransmission(imuAddr);
  Wire.write(reg);
  Wire.write(val);
  return Wire.endTransmission() == 0;
}

bool imuRead(uint8_t reg, uint8_t* buf, size_t n) {
  Wire.beginTransmission(imuAddr);
  Wire.write(reg);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom((int)imuAddr, (int)n) != (int)n) return false;
  for (size_t i = 0; i < n; i++) buf[i] = Wire.read();
  return true;
}

bool imuBegin() {
  Wire.begin(PIN_SDA, PIN_SCL, 100000);
  const uint8_t addrs[] = {0x6A, 0x6B};
  for (uint8_t a : addrs) {
    imuAddr = a;
    uint8_t id = 0;
    if (imuRead(0x0F, &id, 1) && id == 0x6C) { imuOk = true; break; }   // WHO_AM_I
  }
  if (!imuOk) return false;
  imuWrite(0x12, 0x01);              // CTRL3_C: software reset
  delay(20);
  uint8_t c9 = 0;
  imuRead(0x18, &c9, 1);
  imuWrite(0x18, c9 | 0x02);         // CTRL9_XL: disable I3C (as Adafruit does)
  imuWrite(0x12, 0x44);              // CTRL3_C: block data update + address auto-increment
  imuWrite(0x10, 0x40);              // CTRL1_XL: accel 104 Hz, +-4 g
  imuWrite(0x11, 0x40);              // CTRL2_G: gyro 104 Hz, 250 dps
  return true;
}

bool imuReadAll(float a[3], float g[3]) {   // a in m/s^2, g in deg/s (bias removed)
  uint8_t b[12];
  if (!imuRead(0x22, b, 12)) return false;  // OUTX_L_G .. OUTZ_H_A: gyro then accel
  for (int i = 0; i < 3; i++) {
    int16_t gv = (int16_t)(b[2 * i] | (b[2 * i + 1] << 8));
    int16_t av = (int16_t)(b[6 + 2 * i] | (b[7 + 2 * i] << 8));
    g[i] = gv * 8.75e-3f - gyroBias[i];     // 8.75 mdps/LSB at 250 dps
    a[i] = av * 0.122e-3f * 9.80665f;       // 0.122 mg/LSB at +-4 g
  }
  return true;
}

void calibrateGyro(int samples) {   // glove must be still
  if (!imuOk) return;
  float saved[3] = {gyroBias[0], gyroBias[1], gyroBias[2]};
  gyroBias[0] = gyroBias[1] = gyroBias[2] = 0;
  float s[3] = {0, 0, 0}, a[3], g[3];
  int n = 0;
  for (int i = 0; i < samples; i++) {
    if (imuReadAll(a, g)) { s[0] += g[0]; s[1] += g[1]; s[2] += g[2]; n++; }
    delay(5);
  }
  for (int k = 0; k < 3; k++) gyroBias[k] = n ? s[k] / n : saved[k];
}

// Complementary filter: gyro for fast motion, gravity (accelerometer) to stop drift.
bool attValid = false;
float attPitch = 0, attRoll = 0;
uint32_t attUs = 0;
struct TiltSample { uint32_t ms; float pitch, roll; };
TiltSample hist[32];                 // recent zeroed angles, newest at hist[histHead - 1]
int histHead = 0, histCount = 0;
const float RAD = 57.29578f;

void updateTilt() {   // read the IMU once and advance the filter; call every loop
  float a[3], g[3];
  if (!imuOk || !imuReadAll(a, g)) return;   // flaky I2C: skip this sample
  uint32_t nowUs = micros();
  float ap = atan2f(-a[0], sqrtf(a[1] * a[1] + a[2] * a[2])) * RAD;
  float ar = atan2f(a[1], a[2]) * RAD;
  if (!attValid) {
    attPitch = ap;
    attRoll = ar;
    attValid = true;
  } else {
    float dt = (nowUs - attUs) / 1e6f;
    if (dt > 0.2f) dt = 0.2f;
    float p = attPitch / RAD, r = attRoll / RAD;
    float cp = cosf(p);
    if (fabsf(cp) < 0.05f) cp = cp >= 0 ? 0.05f : -0.05f;
    float rollRate = g[0] + (g[1] * sinf(r) + g[2] * cosf(r)) * sinf(p) / cp;   // body rates -> pitch/roll rates
    float pitchRate = g[1] * cosf(r) - g[2] * sinf(r);
    float gp = attPitch + pitchRate * dt;
    float gr = attRoll + rollRate * dt;
    float gmag = sqrtf(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]) / 9.80665f;
    float k = fabsf(gmag - 1) < 0.15f ? TILT_ACCEL_WEIGHT : 0.0f;   // trust gravity less while accelerating
    if (ar - gr > 180) gr += 360;
    else if (gr - ar > 180) gr -= 360;
    attPitch = gp + k * (ap - gp);
    float rr = gr + k * (ar - gr);
    attRoll = rr > 180 ? rr - 360 : rr < -180 ? rr + 360 : rr;
  }
  attUs = nowUs;
  hist[histHead] = {millis(), attPitch - calib.pitch0, attRoll - calib.roll0};
  histHead = (histHead + 1) % 32;
  if (histCount < 32) histCount++;
}

bool tiltAgo(uint32_t agoMs, float& pitch, float& roll) {   // zeroed angles from about agoMs ago
  if (!histCount) return false;
  uint32_t now = millis();
  TiltSample pick = hist[(histHead + 31) % 32];
  for (int k = 0; k < histCount; k++) {
    pick = hist[(histHead + 31 - k) % 32];
    if (now - pick.ms >= agoMs) break;
  }
  pitch = pick.pitch;
  roll = pick.roll;
  return true;
}

// ---------------- calibration (heartbeats keep flowing while busy) ----------------
const char* busyState = "calibrating";
uint32_t busyHb = 0;

void busyWait(uint32_t ms) {   // sleep, but keep the page showing "busy", not "offline"
  uint32_t end = millis() + ms;
  while (true) {
    uint32_t now = millis();
    if (now - busyHb >= HB_MS) {
      float p, r;
      bool ok = tiltAgo(0, p, r);
      char ps[12], rs[12];
      emit("hb", "\"bend\":null,\"state\":\"%s\",\"pitch\":%s,\"roll\":%s,\"imu\":%s", busyState,
           angle(ps, ok, p), angle(rs, ok, r), imuOk ? "true" : "false");
      busyHb = now;
    }
    linkService();
    int32_t left = (int32_t)(end - now);
    if (left <= 0) return;
    delay(left < 20 ? left : 20);
  }
}

float medianOver(uint32_t ms) {
  float v[80];
  int n = 0;
  uint32_t end = millis() + ms;
  while ((int32_t)(end - millis()) > 0 && n < 80) {
    v[n++] = flexMv();
    busyWait(20);
  }
  if (!n) return flexMv();
  std::sort(v, v + n);
  return v[n / 2];
}

void calibrateFlex() {
  busyState = "calibrating";
  while (true) {
    logMsg("CALIBRATE: hold hand OPEN (LED on)");
    setLed(true);
    busyWait(2000);
    float o = medianOver(1000);
    logMsg("CALIBRATE: make a FIST (LED blinking)");
    for (int i = 0; i < 10; i++) { setLed(i % 2); busyWait(200); }
    float f = medianOver(1000);
    setLed(false);
    if (fabsf(f - o) > MIN_CAL_SPAN_MV) {
      calib.open = o;
      calib.fist = f;
      calib.valid = true;
      saveCalib();
      emit("calib", "\"open\":%d,\"fist\":%d", (int)o, (int)f);
      return;
    }
    char t[140];
    snprintf(t, sizeof t, "Calibration failed: open and fist read the same (%d vs %d mV). Check flex wiring. Retrying...", (int)o, (int)f);
    logMsg(t);
  }
}

void zeroTilt() {   // hold the aiming pose still: it becomes pitch 0, roll 0
  if (!imuOk) { logMsg("Zero aim: no IMU"); return; }
  logMsg("ZERO AIM: hold your aiming pose still (LED on)");
  busyState = "zeroing";
  setLed(true);
  busyWait(1500);
  calibrateGyro(100);                // glove is still, so re-measure gyro drift too
  attValid = false;                  // restart the filter from gravity
  float ps = 0, rs = 0;
  int n = 0;
  uint32_t end = millis() + 1000;
  while ((int32_t)(end - millis()) > 0) {
    updateTilt();
    if (attValid) { ps += attPitch; rs += attRoll; n++; }
    busyWait(LOOP_MS);
  }
  setLed(false);
  if (!n) { logMsg("Zero aim failed: IMU not responding"); return; }
  calib.pitch0 = ps / n;
  calib.roll0 = rs / n;
  histCount = 0;
  saveCalib();
  emit("zero", "\"pitch0\":%.2f,\"roll0\":%.2f", calib.pitch0, calib.roll0);
  char t[80];
  snprintf(t, sizeof t, "Aim zeroed (offset pitch %.1f, roll %.1f)", calib.pitch0, calib.roll0);
  logMsg(t);
}

void handleCmd(const String& c) {
  if (c == "cal") calibrateFlex();
  else if (c == "zero") zeroTilt();
  else if (c == "ping") emit("pong");
  if (c == "cal" || c == "zero") {   // drop button presses that queued up while we were busy
    linkService();
    String drop;
    while (popCmd(drop)) {}
  }
}

// ======================= main =======================
enum { HAND_OPEN, HAND_DRAWING } handState = HAND_OPEN;
uint32_t drawStart = 0, lastRelease = 0, lastDrawMsg = 0, lastHb = 0;

void setup() {
  Serial.begin(115200);
  Serial.setTxTimeoutMs(0);          // never block on USB output when no computer is reading it
  if (PIN_LED >= 0) { pinMode(PIN_LED, OUTPUT); setLed(false); }
  linkBegin();
  char t[80];
  snprintf(t, sizeof t, "glove P%d booting (%s, %s)", PLAYER, BOARD_NAME, WIFI_HOST ? "own network" : "phone hotspot");
  logMsg(t);
  if (imuBegin()) {
    logMsg("IMU ok");
    logMsg("Measuring gyro drift: keep the glove still for 1 s");
    calibrateGyro(200);
  } else {
    logMsg("IMU not found - continuing with flex only");
  }
  if (loadCalib()) emit("calib", "\"open\":%d,\"fist\":%d", (int)calib.open, (int)calib.fist);
  else calibrateFlex();
}

void loop() {
  updateTilt();
  uint32_t now = millis();
  float b = bend();

  if (handState == HAND_OPEN) {
    if (b >= CLOSE_AT && now - lastRelease >= COOLDOWN_MS) {
      handState = HAND_DRAWING;
      drawStart = lastDrawMsg = now;
      emit("draw_start");
    }
  } else {
    uint32_t held = now - drawStart;
    float power = std::min(1.0f, held / (float)FULL_DRAW_MS);
    if (b <= OPEN_AT) {
      handState = HAND_OPEN;
      if (held >= MIN_DRAW_MS) {
        float p, r;
        bool ok = tiltAgo(RELEASE_LOOKBACK_MS, p, r);
        char ps[12], rs[12];
        emit("release", "\"power\":%.2f,\"held_ms\":%lu,\"pitch\":%s,\"roll\":%s", power, (unsigned long)held,
             angle(ps, ok, p), angle(rs, ok, r));
        lastRelease = now;
      } else {
        emit("cancel");
      }
    } else if (now - lastDrawMsg >= DRAW_MSG_MS) {
      emit("draw", "\"power\":%.2f", power);
      lastDrawMsg = now;
    }
  }

  if (now - lastHb >= HB_MS) {
    float p, r;
    bool ok = tiltAgo(0, p, r);
    char ps[12], rs[12];
    emit("hb", "\"bend\":%.2f,\"state\":\"%s\",\"pitch\":%s,\"roll\":%s,\"imu\":%s", b,
         handState == HAND_OPEN ? "open" : "drawing", angle(ps, ok, p), angle(rs, ok, r), imuOk ? "true" : "false");
    lastHb = now;
  }

  linkService();
  String c;
  if (popCmd(c)) handleCmd(c);
  delay(LOOP_MS);
}
