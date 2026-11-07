#!/usr/bin/env bash
# Installs Builds/Android/BattalionsInSpace.apk on the phone over Wi-Fi and launches it:
#   bash Tools/deploy.sh
# No address needed. It finds the phone by, in order:
#   1. a phone adb already has connected
#   2. the last address that worked, and port 5555 on every IP the phone has had (both remembered in Tools/.phone)
#   3. adb's own discovery (mDNS; adb also auto-connects a paired phone it hears)
#   4. a port scan of the remembered IPs (up to about a minute per IP, Tools/findport.py)
# After connecting it runs "adb tcpip 5555": the phone then also listens on fixed port 5555 until it reboots, so the
# next deploy connects at once even if Wireless debugging picked a new port.
# Only after a reboot AND an IP change does it need help: pass "IP:PORT" (or just "IP") from the phone's
# Settings > Developer options > Wireless debugging screen; it's remembered. Pairing is once (adb pair IP:PORT CODE).
ADB="/c/Program Files/Unity/Hub/Editor/6000.0.62f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APK="$ROOT/Builds/Android/BattalionsInSpace.apk"
PKG="com.battalions.space"
STATE="$ROOT/Tools/.phone"          # line 1: last IP:PORT that worked; then every IP the phone has had
SEED_IPS="192.168.100.167 192.168.100.188"

[ -f "$APK" ] || { echo "No APK at $APK - build first (Battalions > Build Android)"; exit 1; }
export ADB_MDNS_OPENSCREEN=1        # adb's built-in mDNS (the default needs Bonjour, which this PC doesn't have)
"$ADB" start-server >/dev/null 2>&1

LAST=""; IPS="$SEED_IPS"
if [ -f "$STATE" ]; then LAST=$(head -n 1 "$STATE" | tr -d '\r'); IPS="$(tail -n +2 "$STATE" | tr -d '\r' | tr '\n' ' ') $SEED_IPS"; fi
IPS=$(echo $IPS | tr ' ' '\n' | awk 'NF && !seen[$0]++' | tr '\n' ' ')

online() { "$ADB" devices | tr -d '\r' | awk 'NR>1 && $2=="device" {print $1}' | grep -E '^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+:[0-9]+$' | head -n 1; }
any_online() { "$ADB" devices | tr -d '\r' | awk 'NR>1 && $2=="device" {print $1; exit}'; }
# connect only if the port answers (adb connect to a silent port can hang for a long time)
try() {
  local ip="${1%:*}" port="${1##*:}"
  [ -n "$(python -I "$ROOT/Tools/findport.py" "$ip" "$port")" ] || return 1
  "$ADB" connect "$1" >/dev/null 2>&1 || true
  sleep 0.5
  "$ADB" -s "$1" get-state 2>/dev/null | grep -q device
}

DEV=""
# an address given on the command line
if [ -n "$1" ]; then
  EP="$1"
  if [[ "$EP" != *:* ]]; then echo "Looking for the port on $EP ..."; PORT=$(python -I "$ROOT/Tools/findport.py" "$EP" | awk '{print $1}'); EP="$EP:$PORT"; fi
  try "$EP" && DEV="$EP"
fi
# 1. already connected
[ -n "$DEV" ] || DEV=$(online)
# 2. last address, then port 5555 on known IPs
[ -z "$DEV" ] && [ -n "$LAST" ] && try "$LAST" && DEV="$LAST"
if [ -z "$DEV" ]; then for ip in $IPS; do try "$ip:5555" && { DEV="$ip:5555"; break; }; done; fi
# 3. mDNS: adb connects a paired phone it hears by itself, or we connect what it lists
if [ -z "$DEV" ]; then
  echo "Listening for the phone (mDNS) ..."
  for i in 1 2 3 4 5 6 7 8; do
    DEV=$(any_online); [ -n "$DEV" ] && break
    EP=$("$ADB" mdns services | tr -d '\r' | awk '/_adb-tls-connect/ {print $NF; exit}')
    if [ -n "$EP" ] && try "$EP"; then DEV="$EP"; break; fi
    sleep 1.5
  done
fi
# 4. scan the known IPs
if [ -z "$DEV" ]; then
  for ip in $IPS; do
    echo "Scanning $ip for the wireless-debugging port (up to a minute) ..."
    PORT=$(python -I "$ROOT/Tools/findport.py" "$ip" | awk '{print $1}')
    [ -n "$PORT" ] && try "$ip:$PORT" && { DEV="$ip:$PORT"; break; }
  done
fi
[ -n "$DEV" ] || { echo "Phone not found. Turn on Settings > Developer options > Wireless debugging (same Wi-Fi), or pass its IP:PORT from that screen."; exit 1; }

# keep a fixed port open for next time (until the phone reboots)
IP="${DEV%:*}"
# found by its mDNS name: ask the phone for its Wi-Fi address
if ! [[ "$IP" =~ ^[0-9.]+$ ]]; then IP=$("$ADB" -s "$DEV" shell ip -4 -o addr show wlan0 2>/dev/null | tr -d '\r' | awk '{print $4}' | cut -d/ -f1); fi
if [[ "$IP" =~ ^[0-9.]+$ ]] && [ "$DEV" != "$IP:5555" ]; then
  if "$ADB" -s "$DEV" tcpip 5555 >/dev/null 2>&1; then
    sleep 3
    for i in 1 2 3 4 5; do try "$IP:5555" && { DEV="$IP:5555"; break; }; sleep 1; done
  fi
fi
# forget offline leftovers, remember what worked
for s in $("$ADB" devices | tr -d '\r' | awk 'NR>1 && $2=="offline" {print $1}'); do "$ADB" disconnect "$s" >/dev/null 2>&1 || true; done
if [[ "$IP" =~ ^[0-9.]+$ ]] && [[ "$DEV" == "$IP":* ]]; then { echo "$DEV"; echo "$IP"; echo $IPS | tr ' ' '\n' | grep -vx "$IP"; } | awk 'NF' > "$STATE"; fi

echo "Installing on $DEV ..."
"$ADB" -s "$DEV" install -r -d "$APK" || { echo "Install failed"; exit 1; }
"$ADB" -s "$DEV" shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
echo "Launched $PKG on $DEV"
