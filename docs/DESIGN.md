# Snipperoo design

A tray-only screen recorder and screenshot tool. Press a hotkey, pick an area, and the result (an MP4 under the
Discord size limit, or a PNG) is on the clipboard.

## First run and install

1. Running the downloaded `Snipperoo.exe` opens a three-step wizard (`Ui/SetupWindow`): pick a Discord plan
   (Free 10 MB / Nitro Basic 50 MB / Nitro 500 MB, which sets the size limit) → pick the two shortcuts → done
   (Start with Windows toggle). ffmpeg is checked and, if missing, installed with `winget install Gyan.FFmpeg`
   in the background while the user clicks through.
2. On Finish, `Setup/Installer` copies the exe to `%LOCALAPPDATA%\Programs\Snipperoo` (per user, no admin), adds a
   Start Menu shortcut and an Apps & features entry (`UninstallString` = `Snipperoo.exe --uninstall`), then starts
   the installed copy with `--welcome` and exits.
3. Later runs go straight to the tray. Launching Snipperoo while it runs opens Settings (named events, see
   `SingleInstance`).
4. Uninstall (from Apps & features or Settings) closes the running instance and removes the shortcut, registry
   entries, settings and the install folder. Clips and screenshots are kept.

A dev build (`Snipperoo.dll` next to the exe) skips the install step and runs in place.

## User flow

| Step | Input | Result |
|---|---|---|
| Idle | `Alt+Shift+G` | Selection overlay opens on every monitor (frozen, dimmed screenshot) |
| Selecting | move mouse | The window under the cursor lights up. Over the desktop or taskbar, the whole monitor lights up |
| Selecting | left-drag | A custom rectangle, limited to the monitor the drag started on |
| Selecting | left-click | Start recording the highlighted area |
| Selecting | right-click, `Esc`, or the hotkey | Cancel |
| Recording | `Alt+Shift+G` | Stop. Encoding runs in the background |
| Done | | The MP4 is saved, copied to the clipboard as a file (paste straight into Discord), and a notification is shown |
| Idle | `Alt+Shift+S` | Same selection overlay. Clicking saves that area of the frozen screenshot as a PNG and copies it (as a file and as an image) |

While recording, a thin red frame marks the area and the tray icon gets a red dot (clicking the icon also
stops). The frame and the toasts are excluded from capture (`WDA_EXCLUDEFROMCAPTURE`), so they never appear
in a clip. A toast with a thumbnail confirms each capture. The tray menu has only Open folder, Settings and
Exit.

## Technology choices

- **C# / .NET 10 (LTS).** Win32 interop (hotkeys, window enumeration, DPI, clipboard) is easy from C#. The app
  itself does no heavy work, so a lower-level language would add code without adding speed. It is published as
  one self-contained exe (`Properties/PublishProfiles/win-x64.pubxml`), so users need no .NET runtime.
- **WPF for the windows** (setup, settings, confirm, toasts). The design system is in `Ui/Theme.xaml`: dark palette,
  violet→pink accent from the logo, card radios, toggle switches and keycaps. `ThemedWindow` hides the system
  title bar but keeps the DWM shadow and Windows 11 rounded corners.
- **WinForms for the tray icon and the capture overlay.** Both run inside WPF's message loop (see `Program.cs`
  for the interop setup). The overlay is custom-painted, so WinForms adds no visual baggage there.
- **ffmpeg (external `ffmpeg.exe`) does capture and encoding.** It is found via the configured path, next to the
  exe, on `PATH`, or in winget's `Links` folder (where the setup installs it).
  - Capture: `ddagrab` (Desktop Duplication API) captures on the GPU and crops there, so only the selected
    pixels reach the CPU.
  - Live encode: a hardware H.264 encoder (AMF / NVENC / QSV, probed once at startup), with
    `libx264 ultrafast` as the fallback. This keeps the CPU load low while recording at high quality.
  - Final encode: `libx264` + AAC in MP4 with `+faststart`. H.264 is the codec that embeds and plays
    everywhere Discord runs (desktop, web, iOS, Android).
- **NAudio (WASAPI loopback)** records system audio, since ffmpeg on Windows has no loopback input.
- **Vortice.DXGI** maps a Windows monitor (`\\.\DISPLAYn`) to the DXGI adapter/output index that
  `ddagrab` expects, and reads the output's rotation.

## Pipeline

```
hotkey ─► SelectionSession ─► CaptureRegion ─► Recorder ─┬─ ffmpeg: ddagrab → hwdownload → bgra→nv12 (bt709) → HW H.264 → video.mkv
                                                         └─ NAudio loopback → audio.wav (silence-filled)
stop ───► Recorder.Stop ─► ClipEncoder: probe → EncodePlanner → libx264/AAC → clip.mp4 (size check, retry) ─► clipboard + toast
```

### Capture details

- **One monitor per recording.** `ddagrab` captures one output, so the region is clipped to the monitor
  under the cursor. A window spanning two monitors records only the part on that monitor.
- **Rotated monitors.** `ddagrab` returns the unrotated framebuffer. It is the desktop turned counter-clockwise
  by the DXGI rotation angle (a `ROTATE270` portrait monitor gives a framebuffer turned one quarter clockwise). `CaptureRegion` maps the desktop
  rectangle into framebuffer coordinates for the crop, and the final encode applies a `transpose` to
  turn it upright.
- **Intermediate file:** MKV, so it can still be read if the recording is cut off. The BGRA→NV12 step uses
  BT.709 limited range on the CPU, because the GPU encoders tag colors inconsistently when they get RGB input.
- **Stopping:** `q` on ffmpeg's stdin, which ends the file cleanly.

### Audio sync

Audio and video are recorded by different processes, so their start times must be measured:

- **Audio start:** `AudioRecorder` notes `DateTime.UtcNow` when capture starts. WASAPI loopback sends no
  packets during silence, so gaps are filled with zeros and the WAV position always equals the time since
  that start.
- **Video start:** the capture filter chain contains `setpts=PTS+if(eq(N,0),0*print(RTCTIME,16),0)`. It
  leaves the timestamps unchanged but prints the wall-clock time (µs) when frame 0 passes through.
  `Recorder` reads that line from stderr.
- The final encode skips `videoStart − audioStart` seconds of audio (usually around 110 ms, which is
  ffmpeg's startup time).

Approaches that did not work, with ffmpeg 9.0:
- `ddagrab` timestamps start at 0.
- `-use_wallclock_as_timestamps` and absolute `setpts` values break the muxer.
- Aligning the end points is off by 100–200 ms, because ffmpeg checks stdin for `q` only every 100 ms.

### Size targeting ("best settings under N MB")

`EncodePlanner` is a pure function, unit-tested.

1. Budget = `limit × 0.97` (MP4 overhead). Audio gets 128 kbps, reduced (to a minimum of 32) if that
   would take more than 15% of the budget.
2. Video max rate = `videoBits / (duration + 1 s)`, with `bufsize` = 1 s of max rate. x264's VBV caps total
   bits at `maxrate × duration + bufsize`, so the file stays under the limit.
3. Quality is CRF 18 under that cap: short clips come out small and look clean, and long clips are capped
   by the limit.
4. If bits per pixel per frame is too low, drop 60→30 fps first, then step the resolution down
   (1080→900→720→540→480→360 on the short side). A sharp, lower-resolution clip looks better than a
   blocky full-size one.
5. The x264 preset depends on the total pixels to encode (slow / medium / faster / veryfast), so encoding
   stays a few seconds rather than minutes.
6. After encoding, the real size is checked. If it is over the limit (rare), the clip is re-encoded at a
   proportionally lower rate, up to 2 times.

## Code layout

```
src/Snipperoo/
  Program.cs                entry: uninstall / single instance / setup or tray
  SingleInstance.cs         mutex + "show settings" / "exit" signals between processes
  TrayApp.cs                tray icon, hotkeys, state machine (Idle → Selecting → Recording), toasts
  TrayMenu.cs, TrayIcons.cs dark tray menu; logo icon with status badges
  AppSettings.cs            %APPDATA%\Snipperoo\settings.json
  Log.cs                    %APPDATA%\Snipperoo\snipperoo.log
  Assets/snipperoo.ico      the logo (generated by tools/make_icon.py)
  Setup/                    Installer (copy, shortcut, registry, uninstall), FfmpegInstaller (winget)
  Ui/                       WPF: Theme.xaml, ThemedWindow, SetupWindow, SettingsWindow, Toast, ConfirmDialog,
                            and the controls they share (PlanPicker, ShortcutsEditor, HotkeyBox, SwitchRow, EngineStatus)
  Native/                   P/Invoke, hotkey parsing, hotkey window
  Selection/                overlay forms, window finder, selection session (shared by record and screenshot)
  Capture/                  monitor→DXGI mapping, CaptureRegion, Recorder, AudioRecorder, recording frame
  Encoding/                 ffmpeg locator/runner, encoder probe, EncodePlanner, ClipEncoder
  Output/                   clipboard
tests/Snipperoo.Tests/      EncodePlanner, CaptureRegion mapping, hotkey parsing; PipelineIntegrationTests records
                            and encodes on every monitor (Category=Integration)
```

## Settings (`%APPDATA%\Snipperoo\settings.json`)

Everything except `FrameRate` and `FfmpegPath` is edited in the Settings window, and changes are saved
immediately. While Settings is open the app's hotkeys are unregistered, so the shortcut editor can test whether
a combination is free (`RegisterHotKey`), and pressing one does not start a capture.

| Key | Default | |
|---|---|---|
| `SetupComplete` | `false` | Set when the wizard finishes |
| `Plan` | `Free` | `Free` / `NitroBasic` / `Nitro`: 10 / 50 / 500 MB, counted as decimal MB to be safe |
| `RecordHotkey` | `Alt+Shift+G` | Modifiers: Ctrl, Alt, Shift, Win |
| `ScreenshotHotkey` | `Alt+Shift+S` | Ignored while recording, since the overlay would show up in the video |
| `FrameRate` | `60` | Capture rate. The planner may output 30 for long clips |
| `RecordSystemAudio` | `true` | |
| `ShowCursor` | `true` | |
| `OutputFolder` | `%USERPROFILE%\Videos\Snipperoo` | Clips; screenshots go to its `Screenshots` subfolder |
| `FfmpegPath` | empty | Optional explicit path to `ffmpeg.exe` |

Start with Windows is the HKCU `Run` registry value, not a setting.

## Known limits

- One monitor per recording (see Capture details).
- Windows 11 puts new tray icons in the hidden-icons overflow, and apps cannot pin themselves; the user can drag
  the icon onto the taskbar once.
- Planner thresholds (bits per pixel per frame of 0.06 for 60 fps, 0.04 for resolution) are tuned for desktop/game
  content at H.264 CRF ~18–23. Change them in `EncodePlanner` if clips look soft or blocky.
