# MedReminder MSIX — Assets grafici

## Cosa serve

MSIX richiede loghi PNG multi-size. Nomi e dimensioni sono fissati dal
manifest (`Package.appxmanifest`).

| File | Dimensioni (px) | Uso |
|---|---:|---|
| `Square44x44Logo.png` | 44×44 | Icona taskbar / app list |
| `Square71x71Logo.png` | 71×71 | Small tile |
| `Square150x150Logo.png` | 150×150 | Medium tile (default) |
| `Square310x310Logo.png` | 310×310 | Large tile |
| `Wide310x150Logo.png` | 310×150 | Wide tile |
| `StoreLogo.png` | 50×50 | Store listing + installer |
| `SplashScreen.png` | 620×300 | Splash all'avvio |

**Formato**: PNG con sfondo trasparente (tranne SplashScreen che ha
sfondo blu `#0D47A1` da manifest).

## Come generarli

Opzione 1 — **Visual Studio "Image Asset Generator"** (raccomandato):
apre una MSIX packaging project, doppio click su `Assets\...`,
"Generate all assets". Genera tutte le taglie da una source PNG 400×400.

Opzione 2 — **CLI con ImageMagick** partendo da
`assets/medreminder.ico` (multi-res 256×256 max):

```bash
# Estrae 256×256 dalla ico
magick convert assets/medreminder.ico[0] -resize 256x256 base.png

# Genera le taglie MSIX
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

Opzione 3 — **build-msix.ps1** (nella cartella `packaging/scripts/`)
prova a rigenerare gli asset mancanti se `magick.exe` è nel PATH,
altrimenti fallisce con un messaggio chiaro.

## Verifica

Dopo la build, `MakeAppx.exe pack` fallisce se un asset dichiarato nel
manifest manca. Il pacchetto MSIX generato può essere ispezionato con:

```bat
MakeAppx.exe unpack /p MedReminder-1.0.1-x64.msix /d unpacked
```
