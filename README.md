# SmartCamLight

A small Windows tray app that turns your **Logitech Litra** light on when your camera starts and off when it stops. It also gives you full control over the light and your webcam's picture settings, with no Logi Tune needed.

![Light page](docs/light.png)

## Why?

Logi Tune's *Smart Activation* stopped working on recent Windows 11 builds. Older tools detect "camera on" from the privacy registry (`CapabilityAccessManager\ConsentStore\webcam`), which current Windows builds no longer update reliably. SmartCamLight uses Windows' **Media Foundation Sensor Activity Monitor** instead: Windows notifies it as soon as any app starts or stops streaming from a camera. It works with Zoom, Teams, Discord, browsers, OBS and everything else.

## Features

- **Smart activation**: the light turns on when any app starts using a camera, and off when it stops, after a delay you choose.
- **Light control**: power, brightness and colour temperature (2700–6500 K), plus presets. Available from the settings window or the tray menu.
- **Webcam settings**: brightness, contrast, saturation, hue, sharpness, white balance, exposure, gain, backlight compensation, zoom, focus, pan and tilt, with Auto where the camera supports it. Your values are re-applied whenever the camera reconnects.
- **Ignore list**: apps whose camera use shouldn't trigger the light.
- **Starts with Windows**, quietly in the tray.
- Follows Windows dark or light mode.
- One small `.exe` with no installer, no runtime to install and no background services.

![Camera page](docs/camera.png)

## Supported devices

| Device | Support |
| --- | --- |
| Logitech Litra Glow | ✅ Tested |
| Logitech Litra Beam | ✅ Same protocol (untested) |
| Logitech Litra Beam LX, other lights | ❌ Not yet (contributions welcome) |
| Webcams | Smart activation works with any camera. Picture settings work with any standard USB (UVC) webcam. Tested with the OBSBOT Meet 2. |

Requires Windows 10 (2004 or later) or Windows 11. Developed on Windows 11 build 26200.

## Install

1. Download `SmartCamLight.exe` from the [latest release](../../releases/latest).
2. Put it somewhere permanent, for example `%LOCALAPPDATA%\Programs\SmartCamLight\`.
3. Run it. The app is unsigned, so Windows SmartScreen may warn you: click **More info → Run anyway**. You can also build it yourself from source (see below).
4. Click the tray icon to open the settings. Turn on **Start with Windows** on the General page.

If Logi Tune is installed, quit it or turn off its Smart Activation so the two apps don't fight over the light.

## Build from source

```powershell
.\build.ps1
```

This uses the C# compiler that ships with Windows (.NET Framework 4.8), so there's nothing to install. The output is `bin\SmartCamLight.exe`.

## How it works

| Part | File |
| --- | --- |
| Camera detection (Media Foundation sensor activity monitor, registry fallback) | `src/SensorMonitor.cs`, `src/CameraUsage.cs` |
| Litra control over raw HID | `src/Litra.cs` |
| Webcam settings through DirectShow (`IAMVideoProcAmp` / `IAMCameraControl`) | `src/UvcCamera.cs` |
| Tray app and smart activation logic | `src/TrayApp.cs` |
| Settings window and custom controls | `src/SettingsForm.cs`, `src/Controls.cs`, `src/Ui.cs` |

Settings and the log are stored in `%APPDATA%\SmartCamLight\`.

## Limitations

- OBSBOT-specific features (AI tracking and framing, HDR, gestures) use a private vendor protocol. Use OBSBOT Center for those.
- The camera's own picture settings are standard UVC controls. What's available depends on the camera.

## Credits

The Litra HID commands were first documented by the open-source community, notably [timrogers/litra](https://github.com/timrogers/litra).

## License

[MIT](LICENSE)

*Not affiliated with Logitech or OBSBOT. Product names are trademarks of their respective owners.*
