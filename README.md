# Steal an Egg — Smart AFK Fishing & Treadmill Bot (v5.0)

[![Release Build](https://github.com/sharych/StealAnEgg-AFK-Bot/actions/workflows/release.yml/badge.svg)](https://github.com/sharych/StealAnEgg-AFK-Bot/actions/workflows/release.yml)

An external, screen-reading AFK automation GUI for Roblox **Steal an Egg** available for both **Windows** and **macOS** (Apple Silicon & Intel).

> **Safety Note:** 100% external. Uses standard OS screen capture and mouse/keyboard events with zero memory reading or injection into Roblox.

---

## Table of Contents
1. [Download & Installation](#download--installation)
2. [How to Use — AFK Fishing (`F6`)](#how-to-use--afk-fishing-f6)
3. [How to Use — Treadmill Speed AFK (`F8`)](#how-to-use--treadmill-speed-afk-f8)
4. [Hotkeys & Controls](#hotkeys--controls)
5. [Default Settings Reference](#default-settings-reference)
6. [Troubleshooting](#troubleshooting)
7. [Building from Source](#building-from-source)

---

## Download & Installation

Download the latest ready-to-use files from the **[Releases](https://github.com/sharych/StealAnEgg-AFK-Bot/releases)** page:

### Windows (Windows 10 / 11)
1. Download **`StealAnEgg_AFK_Fishing_Bot_Windows.exe`**.
2. Double-click to run (no Python, Java, or external runtime install needed).
3. *If Windows SmartScreen appears:* click **More info** -> **Run anyway** (the `.exe` is unsigned open-source software; you can inspect or compile [StealAnEgg_AFK_Bot.cs](windows/StealAnEgg_AFK_Bot.cs) yourself).

### macOS (macOS 14 Sonoma / macOS 15 Sequoia+)
1. Download and unzip **`StealAnEgg_AFK_Bot_macOS.app.zip`**.
2. Move **`StealAnEgg_AFK_Bot.app`** to your **Applications** folder or Desktop.
3. On first launch, if macOS says it is from an unidentified developer: **Right-click -> Open -> Open** (or run `xattr -cr /Applications/StealAnEgg_AFK_Bot.app` in Terminal).
4. Grant the required permissions in **System Settings -> Privacy & Security**:
   - **Accessibility** -> Turn ON `StealAnEgg_AFK_Bot` *(allows clicks, key presses, and camera rotation)*
   - **Screen Recording** -> Turn ON `StealAnEgg_AFK_Bot` *(allows detecting the `STOP` button in the fishing bar)*
5. Restart the app once after toggling permissions.

---

## How to Use — AFK Fishing (`F6`)

### 1. In-Game Setup
- Equip your **Fishing Rod in Hotbar Slot `9`** (and keep your swap item/egg in **Slot `3`** if using the **2x Hotbar Swap Glitch**, or change those keys on Tab 2).
- Stand facing the water with the rod **held in hand (NOT cast yet)**.
- Place the bot window in the **top-left or top-right corner** so it never covers the fishing water point or the bottom-right minigame `STOP` button area.

### 2. First-Time Aim Check
- By default, **Tab 2 -> Cast Aim Coordinates** are set to `X: 1138, Y: 362` (tuned for `1920x1080` fullscreen/maximized Roblox).
- If you play on a different monitor resolution, macOS Retina display, or windowed layout:
  1. Go to **Tab 2 -> Casting Settings**.
  2. Click **`Pick Point (3s Countdown)`**.
  3. Move your cursor over the water in Roblox and wait for the beep — your exact coordinates save automatically.
- On **Tab 1**, click **`Test Radar Scan Right Now`** during normal gameplay to confirm it reports `NO (0 red px)` so nothing on your screen causes false triggers.

### 3. Start AFK Fishing
- Focus Roblox (or stay anywhere) and press **`F6`** (or click **`START FISHING (F6)`**).
- The bot automatically:
  1. Executes the optional `9 -> 3` hotbar glitch swap (`ON` by default).
  2. Holds Left-Click for **`1.10s`** to hit a `PERFECT!` cast.
  3. Watches the minigame radar for the red `STOP` button (catches **any random fish**).
  4. Turbo-clicks while `STOP` is visible and **stops the instant the catch completes** (no stray idle quick-casts).
  5. Waits **`2.50s`** for the reward popup, then repeats cleanly.
  6. Every random **`45–110s`** (between casts), gently rotates and returns the camera so Roblox never kicks you for 20-minute inactivity.
- Press **`F6`** anytime to stop immediately.

---

## How to Use — Treadmill Speed AFK (`F8`)

Designed specifically so your character **never steps off or resets on the treadmill**:

1. Walk onto your treadmill in **Steal an Egg** and confirm your character is running and gaining Speed.
2. *(Optional)* Switch to **Tab 3 -> Treadmill & Camera Anti-AFK** and click **`Test Smooth Camera Rotate (2s Delay)`** -> immediately switch to Roblox to preview the smooth camera sweep.
3. Press **`F8`** (or click **`TREADMILL AFK (F8)`**).
4. **Zero `WASD` movement keys are ever pressed.** Every randomized **`45–110` seconds**, the bot smoothly Right-Click drags the camera left or right, pauses briefly, and smoothly returns to the exact starting angle (plus an optional `<` / `>` arrow tap).
5. Press **`F8`** anytime to stop.

> **Important for Overnight AFK:** Set your PC/Mac **Screen Sleep / Turn off display** timer to **Never** while running AFK Fishing, because Windows/macOS stops updating screen pixels once the monitor sleeps.

---

## Hotkeys & Controls

| Global Hotkey | Action |
| --- | --- |
| **`F6`** | Start / Stop **AFK Fishing** |
| **`F7`** | **Instant Test 20 Clicks** (tests click registration inside Roblox) |
| **`F8`** | Start / Stop **Treadmill Speed AFK** |

---

## Default Settings Reference

| Tab | Setting | Default | Notes |
| --- | --- | --- | --- |
| Tab 1 | **Bite Timeout** | `9.00s` | Max wait before resetting empty line *(increase to ~`20s` if 2x Glitch is OFF)* |
| Tab 1 | **Max Click Safety Cap** | `48` clicks | Hard ceiling per fish if minigame sticks |
| Tab 1 | **Click Speed** | `30ms` | Delay between reel clicks |
| Tab 1 | **Post-Catch Wait** | `2.50s` | Delay for reward popup to finish before next cast |
| Tab 2 | **Cast Aim Coordinates** | `X: 1138, Y: 362` | Water target point (`1920x1080` default — use `Pick Point` on Mac/other sizes) |
| Tab 2 | **Cast Hold Duration** | `1.10s` | Charge time for `PERFECT!` cast |
| Tab 2 | **Hotbar Key Swap Glitch** | `ON` (`9` & `3`) | Rod key `9`, swap item key `3` |
| Tab 3 | **Humanize Timings** | `ON` | Random `±3 px` aim & `±25 ms` cast variance |
| Tab 3 | **Camera Rotate Anti-AFK** | `ON` (`45–110s`) | Smooth Right-Click drag (`120 px`) & return |
| Tab 3 | **Arrow Key Assist** | `ON` | Gentle `<` / `>` camera tap during rotation |

---

## Troubleshooting

- **Bot casts, pulls back immediately, or misses bites:**
  - Make sure your rod is held in hand **not yet cast** before pressing `F6`.
  - If **2x Glitch** is OFF, increase **Bite Timeout** from `9.00s` to `18.00–22.00s` because unboosted bites take longer.
- **Radar says `NO` during minigame or triggers constantly:**
  - Ensure nothing covers the bottom-right region of your screen (`65%–85%` width, `80%–95%` height).
  - On macOS, double-check **Screen Recording** permission is enabled and restart the app.
- **Clicks/keys don't affect Roblox on Mac:**
  - Enable **Accessibility** permission for `StealAnEgg_AFK_Bot` in **System Settings -> Privacy & Security -> Accessibility**.

---

## Building from Source

### Windows
Uses the built-in .NET Framework 4 compiler (`csc.exe`, preinstalled on Windows 10/11):
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
This outputs a Universal (`arm64` + `x86_64`) app bundle at `macos/build/StealAnEgg_AFK_Bot.app`.
