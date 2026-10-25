// Minimal Arduino WiFi test: can phones join a network hosted by this board?
// Hosts "BowDuelTest" (password bowduel26) at 192.168.4.1 with a one-line page,
// and prints every join/leave on USB serial.
#include <WiFi.h>
#include <WebServer.h>

WebServer server(80);
int lastCount = -1;

void setup() {
  Serial.begin(115200);
  delay(500);
  WiFi.mode(WIFI_AP);
  WiFi.softAPConfig(IPAddress(192, 168, 4, 1), IPAddress(192, 168, 4, 1), IPAddress(255, 255, 255, 0));
  bool ok = WiFi.softAP("BowDuelTest", "bowduel26", 6, 0, 4);
  server.on("/", []() {
    server.send(200, "text/html",
                "<meta name=viewport content='width=device-width'>"
                "<h1 style='font-family:sans-serif'>BowDuelTest: Arduino WiFi works</h1>");
  });
  server.begin();
  Serial.printf("softAP %s, IP %s\n", ok ? "OK" : "FAILED", WiFi.softAPIP().toString().c_str());
}

void loop() {
  server.handleClient();
  int n = WiFi.softAPgetStationNum();
  if (n != lastCount) {
    Serial.printf("[%lus] devices on BowDuelTest: %d\n", millis() / 1000, n);
    lastCount = n;
  }
  delay(5);
}
