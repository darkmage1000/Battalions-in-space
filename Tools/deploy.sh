#!/usr/bin/env bash
# Installs Builds/Android/BattalionsInSpace.apk on the paired phone over wireless debugging and launches it.
# The phone's connect port changes after reboot / Wi-Fi toggle; it is rediscovered via mDNS.
# Pairing only has to be done once (adb pair IP:PORT CODE).
set -e
ADB="/c/Program Files/Unity/Hub/Editor/6000.0.62f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APK="$ROOT/Builds/Android/BattalionsInSpace.apk"
PKG="com.battalions.space"

[ -f "$APK" ] || { echo "No APK at $APK - build first (Battalions > Build Android)"; exit 1; }
"$ADB" start-server >/dev/null 2>&1
EP=""
for i in 1 2 3 4 5 6 7 8; do
  EP=$("$ADB" mdns services | awk '/_adb-tls-connect/ {print $3; exit}')
  [ -n "$EP" ] && break
  sleep 1.5
done
if [ -n "$EP" ]; then "$ADB" connect "$EP" >/dev/null || true; fi
DEV=$("$ADB" devices | awk 'NR>1 && $2=="device" {print $1; exit}')
[ -n "$DEV" ] || { echo "Phone not found. Turn on Settings > Developer options > Wireless debugging (same Wi-Fi)."; exit 1; }
echo "Installing on $DEV ..."
"$ADB" -s "$DEV" install -r -d "$APK"
"$ADB" -s "$DEV" shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null
echo "Launched $PKG on $DEV"
