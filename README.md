<p align="center"><img src="assets/glint-256.png" width="96" alt="Glint"></p>

# Glint

A small, fast screenshot tool for Windows. One exe, under 300 KB, no installer required and nothing extra to install.

## What it does

- Area, window, fullscreen, delayed and scrolling captures. Print Screen starts one.
- Window capture ignores whatever is on top of the window.
- Editor with arrows, text, shapes, steps, blur, pixelate, erase, magnifier, ruler and backdrops.
- Floating preview, pin to screen, OCR, color picker with WCAG and APCA contrast.
- Uploads to Zipline, Cloudflare R2, S3, a Discord webhook or your own endpoint, with a hotkey per target. ShareX `.sxcu` files can be imported.
- PNG, JPEG and lossless WebP.
- English and German.

## Get it

Grab `Glint.exe` from [Releases](https://github.com/caneabi/glint/releases). It offers to install itself on first start, or just runs from wherever you put it.

- **Stable** is the latest tagged release.
- **Nightly** is built from the `nightly` branch and can break. Switch channels in Settings > General.

Glint tells you about updates and opens the download page. It never installs anything by itself. The exe isn't signed yet, so SmartScreen may complain.

## Build

You need Windows and the [.NET SDK](https://dotnet.microsoft.com/download). It targets .NET Framework 4.8, which ships with Windows.

```
./build.ps1
```

## Releasing

New work goes to `nightly`, which CI builds once a day when it changed. For a stable release, merge into `main` and push a tag like `v0.2.0`.

## Credits

Icons by [Lucide](https://lucide.dev), see [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES).
