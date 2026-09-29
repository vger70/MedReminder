# MedReminder MSIX — Graphic Assets

## What is required

MSIX requires multi-size PNG logos. Names and dimensions are fixed by
the manifest (`Package.appxmanifest`).

| File | Size (px) | Use |
|---|---:|---|
| `Square44x44Logo.png` | 44×44 | Taskbar / app list icon |
| `Square71x71Logo.png` | 71×71 | Small tile |
| `Square150x150Logo.png` | 150×150 | Medium tile (default) |
| `Square310x310Logo.png` | 310×310 | Large tile |
| `Wide310x150Logo.png` | 310×150 | Wide tile |
| `StoreLogo.png` | 50×50 | Store listing + installer |
| `SplashScreen.png` | 620×300 | Splash screen at startup |

**Format**: PNG with transparent background (except SplashScreen, which
has a blue background `#0D47A1` set by the manifest).

## How to generate them

Option 1 — **Visual Studio "Image Asset Generator"** (recommended):
open an MSIX packaging project, double-click on `Assets\...`,
"Generate all assets". Produces every size from a single 400×400 source
PNG.

Option 2 — **CLI with ImageMagick**, starting from
`assets/medreminder.ico` (multi-res, up to 256×256):

```bash
# Extract the 256×256 frame from the ico
magick convert assets/medreminder.ico[0] -resize 256x256 base.png

# Generate the MSIX sizes
for size in 44 71 150 310; do
  magick convert base.png -resize ${size}x${size} \
    packaging/msix/Assets/Square${size}x${size}Logo.png
done
magick convert base.png -resize 310x150 -gravity center -background none \
  -extent 310x150 packaging/msix/Assets/Wide310x150Logo.png
magick convert base.png -resize 50x50 \
  packaging/msix/Assets/StoreLogo.png
magick convert base.png -resize 620x300 -gravity center -background "#0D47A1" \
  -extent 620x300 packaging/msix/Assets/SplashScreen.png
```

Option 3 — **build-msix.ps1** (in the `packaging/scripts/` folder)
tries to regenerate any missing asset if `magick.exe` is on the PATH,
otherwise it fails with a clear error message.

## Verification

After the build, `MakeAppx.exe pack` fails if an asset declared in the
manifest is missing. The generated MSIX package can be inspected with:

```bat
MakeAppx.exe unpack /p MedReminder-1.0.1-x64.msix /d unpacked
```
