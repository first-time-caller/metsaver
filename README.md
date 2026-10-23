# MetSaver

A Windows screensaver that fills your displays with public-domain paintings from
[The Metropolitan Museum of Art](https://www.metmuseum.org/) - full-screen, slowly drifting, with a small
frosted-glass card naming the work.

* Picks paintings that **suit your screen**: checks the Met's metadata (medium, physical size, shape) and
  the image's pixel size *before* downloading the full file.
* **Every monitor** gets its own painting, chosen for that monitor's shape.
* Crossfades, optional **Ken Burns** zoom/pan, glass info card tinted from the painting's colours.
* Remembers what it has shown (no repeats), caches recent paintings so it works offline.
* Per-user install, **no admin rights**, no account, no telemetry.

## Install

1. Download **`MetSaver-Setup.exe`** from the [latest release](../../releases/latest) and run it.
   (Windows SmartScreen may say "unknown publisher" - the app isn't code-signed yet. *More info → Run anyway*.
   Every release lists SHA-256 checksums in `SHA256SUMS.txt`.)
2. Leave "Make MetSaver my screensaver" ticked. It starts after 3 minutes idle - change that in settings.

Prefer no installer? Download `MetSaver.scr`, run it once, and click **Set as my screensaver** in the settings
window. (`MetSaver-small.scr` is ~1 MB but needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).)

**Uninstall:** *Settings → Apps → MetSaver screensaver → Uninstall* (your previous screensaver is restored), or
open settings and click **Restore previous**.

## Settings

Open from Start → *MetSaver settings*, or Windows' screensaver dialog → *Settings*.

| Setting | What it does |
|---|---|
| Start after idle | Seconds of inactivity before Windows launches it |
| Seconds per painting | How long each painting stays up |
| Painting size | *Fill the screen* (crops edges) or *Whole painting on a mat* |
| Shape match | Higher = only paintings that already match your screen shape (less cropping, fewer candidates) |
| Motion | Off, subtle drift, or Ken Burns |
| Info card | Show/hide, corner, and how long before it fades away |
| Collections / themes | Which Met departments and search words to draw from |

**Test window** previews it without waiting for the timer. Everything lives in `%LOCALAPPDATA%\MetSaver`
(`config.json`, `cache\`, `seen.json`, `log.txt`) - if something looks wrong, `log.txt` shows why each
candidate painting was accepted or skipped.

Command line: `MetSaver.exe /test`, `/install [seconds]`, `/uninstall`, `/seed`, `/c`.

## How paintings are chosen (the catalog)

Searching and vetting the Met live is slow and wasteful, so MetSaver ships with a **pre-built, pre-vetted catalog**
(`data/catalog.json.gz`, built by `tools/CatalogBuilder`): every public-domain painting that passes the
screen-independent checks, with its physical size, pixel size and file size read from the image header.
At run time the app just filters that list for *your* screen's shape and resolution, picks one you haven't seen, and
downloads that single image - the first painting appears in a few seconds, with a progress bar and time estimate
while it loads. If the catalog is missing it falls back to searching the API live.

* The catalog is rebuilt monthly by `.github/workflows/catalog.yml` and published as a release asset; set
  `"catalog_url"` in `config.json` to that asset's URL and the app quietly refreshes its copy (at most weekly).
* Rebuild it yourself: `dotnet run --project tools/CatalogBuilder -- data/catalog.json.gz` (takes a while by design -
  it keeps to ~8 requests/second out of respect for the Met's API).

## Smart features

* **Smart crop** - filling the screen means cropping; MetSaver slides the crop toward the busiest part of the painting
  instead of trimming equally from both sides.
* **Hour and season** - night scenes are favoured after dark, snow in winter, sunrise in the morning (from titles; an
  explainable heuristic, not a model). Turn off with `"time_of_day": false`.
* **Ambient dimming** - dims with the ambient light sensor if the machine has one, otherwise gently late at night; on
  battery the motion is dialled down a notch.
* **Per-monitor choice** - each monitor gets paintings whose shape suits it (portrait monitors get portrait paintings).
* **Gallery mode** - `MetSaver.exe /gallery` (or *Open gallery* in settings): full-screen, driven by the keyboard.
  `I` details (artist bio, size, credit line, gallery location), `F` favourite (favourites appear more often),
  `O` open the painting on metmuseum.org, `N` next, `Esc` exit.

## macOS (experimental)

A native Swift screensaver lives in `macos/`: the same catalog, the same vetting, mood weighting, smart crop and glass info
card, built as a universal (Apple silicon + Intel) `MetSaver.saver`.

* **Install:** download `MetSaver-mac.dmg` from the release, double-click `MetSaver.saver`, and choose it in
  *System Settings > Screen Saver*. Options are under *Screen Saver Options*.
* **Gatekeeper:** until the release is signed and notarized with an Apple Developer ID, macOS will say it can't verify the
  developer. *System Settings > Privacy & Security > Open Anyway*, then double-click again. (The release workflow signs and
  notarizes automatically once the repository has the secrets listed in `.github/workflows/macos.yml`.)
* **Differences from Windows:** macOS screensavers receive no keyboard input, so there is no gallery mode, favourites key or
  details panel; there is no public light-sensor API, so dimming follows the clock; each display gets its own painting.
* **Status:** written without access to a Mac and compiled and unit-tested only by CI so far - please report what you see
  (`~/Library/Application Support/MetSaver/log.txt`, or inside the legacyScreenSaver container on macOS 14+).
* **Build:** `brew install xcodegen && bash macos/scripts/build-saver.sh`; tests: `swift test --package-path macos`.

## Privacy

MetSaver talks only to `collectionapi.metmuseum.org` (metadata) and `images.metmuseum.org` (pictures), using the Met's
public Open Access API. It sends no personal data and has no analytics. Source is here - read it.

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```powershell
dotnet test MetSaver.sln
dotnet run --project src/MetSaver -- /test
dotnet publish src/MetSaver -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

Layout: `src/MetSaver.Core` (settings, Met API client, painting vetting - platform-independent and unit-tested),
`src/MetSaver` (WPF app: rendering, windows, Windows integration), `tools/CatalogBuilder` (builds the catalog), `installer/` (Inno Setup script),
`.github/workflows/build.yml` (tests on every push; pushing a tag like `v1.0.0` builds and publishes a release).

## Credits and licence

Code: MIT (see `LICENSE`). Artworks and data: [The Met Open Access](https://www.metmuseum.org/about-the-met/policies-and-documents/open-access)
programme, CC0 - images shown are only those the Met marks as public domain, and each is credited on screen.
This is an independent project, not affiliated with or endorsed by The Metropolitan Museum of Art.
