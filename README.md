# VTRIMZ

Lightweight Windows video editor — splash screen, media import, playback controls, timeline with split/zoom/screenshot.

**Developed by Shazzad Hossen**

## Features

- Splash screen with app branding
- Import any common video format (MP4, MKV, AVI, MOV, WMV, WebM, and more)
- Smooth hardware-accelerated playback (LibVLC)
- Play / Pause, previous frame, next frame, volume control
- Interactive timeline with draggable playhead marker
- Split track at marker position
- Timeline zoom in / out
- Screenshot capture at current frame

## Requirements

- Windows 10 or later (64-bit)
- No separate installs needed — everything is bundled in the executable

## Build & Run (Development)

```bash
cd Vtrimz
dotnet run
```

## Publish — single shareable EXE

Double-click `publish.bat` or run:

```bash
dotnet publish Vtrimz/Vtrimz.csproj -c Release -o publish
```

Output: **`publish/VTRIMZ.exe`** (~190 MB) — শুধু এই একটা ফাইল share করুন।

- Windows 10/11 **64-bit**-এ double-click করলেই চলবে
- .NET / VLC / অন্য কিছু install করতে হবে না
- প্রথম **Export**-এ একবার internet লাগতে পারে (FFmpeg auto-download)

## Tech Stack

- .NET 9 WPF
- LibVLC (bundled via NuGet — no manual FFmpeg/VLC install required)
