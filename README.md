# Steal an Egg — Smart AFK Fishing & Treadmill Bot (v5.0)

An external, screen-reading AFK automation GUI for Roblox **Steal an Egg** available for both **Windows** and **macOS** (Apple Silicon & Intel).

> **Note:** Uses standard OS screen capture and mouse/keyboard events with zero memory injection.

---

## Features

### 1. Precision Sync AFK Fishing (`F6`)
- **Color-Radar Bite Detection:** Scans the bottom-right minigame bar for the red `STOP` button, catching any fish type cleanly without hardcoded fish sprites.
- **Instant Stop on Catch:** Stops clicking the millisecond the `STOP` button disappears so no stray clicks trigger unwanted quick-casts.
- **Tuned Defaults Preloaded:**
  - Bite Timeout: `9.00s` (with `2x Hotbar Key Swap Glitch` enabled on keys `9` & `3`)
  - Max Click Cap: `48` clicks | Click Speed: `30ms`
  - Post-Catch Wait: `2.50s`
  - Cast Hold Duration: `1.10s` (`PERFECT!` cast)
- **Missed-Bite Auto Recovery:** Automatically resets the line if a cast times out without a bite.

### 2. Treadmill Speed AFK Mode (`F8`)
- Built specifically so your character **never steps off the treadmill**.
- Zero `WASD` movement keys pressed.
- Every randomized **`45–110` seconds** (configurable), performs a **smooth Right-Click camera sweep & return** (with optional `<` / `>` arrow key assist) so Roblox keeps registering activity without moving your character's feet.

### 3. Humanized Timings & Safety
- Small random coordinate jitter (`±3 px`) and hold-duration variance (`±25 ms`) on every cast.
- Live Radar Preview & **Test Radar Scan** / **Instant Test 20 Clicks (`F7`)** buttons.

---

## Download (Windows & macOS)

Check the **[Releases](../../releases)** page for pre-built binaries:
- **Windows:** `StealAnEgg_AFK_Fishing_Bot_Windows.exe` (No installation needed — Windows 10/11)
- **macOS:** `StealAnEgg_AFK_Bot_macOS.app.zip` (Universal Binary — macOS 14 Sonoma / macOS 15 Sequoia+)

### macOS First-Time Setup
1. Unzip `StealAnEgg_AFK_Bot_macOS.app.zip` and move `StealAnEgg_AFK_Bot.app` to Applications (or Desktop).
2. If macOS Gatekeeper warns about an unidentified developer, right-click the app -> **Open** (or run `xattr -cr StealAnEgg_AFK_Bot.app` in Terminal).
3. Grant permissions in **System Settings -> Privacy & Security**:
   - **Accessibility** (allows mouse clicks, camera drags, and key presses)
   - **Screen Recording** (allows scanning the `STOP` button in Roblox)
4. Use **Pick Point (3s Countdown)** on Tab 2 once to match your Mac screen's water coordinates.

---

## Hotkeys
| Key | Action |
| --- | --- |
| `F6` | Start / Stop AFK Fishing |
| `F7` | Instant Test 20 Turbo Clicks |
| `F8` | Start / Stop Treadmill Speed AFK |

---

## Building from Source

### Windows
Requires standard Windows .NET Framework 4 (`csc.exe`, built into Windows 10/11):
```bat
cd windows
build_windows.bat
```

### macOS
Requires Xcode Command Line Tools (`xcode-select --install`) on macOS 14+:
```bash
chmod +x macos/build_mac.sh
./macos/build_mac.sh
```
