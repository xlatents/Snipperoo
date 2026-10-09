# Snipperoo

Screen clips and screenshots for Discord: a small tray app for Windows, much simpler than ShareX.

| Keys (default) | What it does |
|---|---|
| `Alt+Shift+G` | Pick an area, then record it. Press again (or click the tray icon) to stop |
| `Alt+Shift+S` | Pick an area, then take a screenshot |

When picking an area: hover to highlight a window (or the whole monitor over the desktop), **left-click** to take
it, **left-drag** to mark your own rectangle, and **right-click** or **Esc** to cancel.

A clip is encoded to fit your Discord upload limit at the best quality that fits. It is saved to
`Videos\Snipperoo` and **copied to the clipboard as a file**, so Ctrl+V in Discord attaches it. Screenshots go to
`Videos\Snipperoo\Screenshots` and are copied too.

## Install

Run `Snipperoo.exe`. A short setup asks for your Discord plan and your shortcuts, then installs Snipperoo for your
user account (no admin rights needed) and puts it in the tray. It also sets up ffmpeg through winget if it is
missing. Uninstall it from Windows Settings → Apps, or from Snipperoo's Settings.

Windows 11 hides new tray icons under the ^ arrow; drag Snipperoo's icon onto the taskbar to keep it visible.

## Build

```
dotnet publish src/Snipperoo -p:PublishProfile=win-x64    # → publish\Snipperoo.exe (self-contained, one file)
```

Running the Debug build (`dotnet run --project src/Snipperoo`) skips the install step.

## Tests

```
dotnet test --filter "Category!=Integration"   # unit tests
dotnet test --filter Category=Integration        # records ~4 s on each monitor and encodes it
```

Logs go to `%APPDATA%\Snipperoo\snipperoo.log`. How it works, and why: [docs/DESIGN.md](docs/DESIGN.md).
