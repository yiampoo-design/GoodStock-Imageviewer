# WpfApp1 -- GoodStock Image Viewer

WPF image viewer with EXIF metadata read/write via ExifTool.

## Features

- Thumbnail grid display
- Fullscreen image viewer
- EXIF Info tab -- camera, focal length, f-stop, shutter speed, ISO, lens, date taken
- Metadata tab -- file properties + EXIF metadata
- MetadataDialog -- edit title, description, artist, copyright, keywords and save back to file
- Auto-download ExifTool on first run (no manual install)
- Histogram tab (RGB)
- Crop / Draw overlays
- Folder tree navigation
- Search / Sort

## Architecture

```
WpfApp1/
  ExifToolRunner.cs        -- Run ExifTool process with UTF-8 argument file
  ExifToolService.cs       -- Discover, download, verify ExifTool binary
  MainWindow.xaml/.cs      -- Main UI, EXIF Info tab, Metadata tab
  MetadataDialog.xaml/.cs  -- Edit metadata dialog (read/write via ExifTool)
  Chrome.xaml              -- Custom window chrome
  App.xaml/.cs             -- Application entry
  WpfApp1.csproj           -- .NET 8 WPF project (zero NuGet dependencies)
```

---

## Graphic Resources

The app uses **100% inline vector graphics** -- no external image files (.png, .ico, .jpg, .svg) in the project. All icons are defined as XAML Path geometry or Segoe MDL2 Assets font glyphs.

### App Logo

| Element | Location | Description |
|---|---|---|
| Title bar logo | `MainWindow.xaml` line 497-507 | 22x22px rounded rect, gradient `#3B82F6` to `#A855F7` (blue to purple), white text "GS" 8px Bold |
| Viewer title logo | `MainWindow.xaml` line 1544-1553 | 18x18px same design |

### Folder Icons (DrawingImage resources)

| Icon | Resource Key | Colors | Description |
|---|---|---|---|
| Folder (closed) | `FolderIcon` | `#FFA000` (tab) + `#FFCA28` (body) | Standard folder with tab |
| Folder (open) | `FolderOpenIcon` | `#FFA000` + `#FFCA28` + `#FFD54F` (inner) | Open folder showing inside |

### Toolbar Icons (Viewbox 18x18, Canvas 24x24)

All toolbar icons use:
- Stroke: `{StaticResource TextSecondaryBrush}` = `#8B949E`
- StrokeThickness: 1.7
- StrokeLineJoin: Round
- StrokeStartLineCap: Round
- StrokeEndLineCap: Round
- Fill: Transparent

| Icon | Line | SVG Path Data |
|---|---|---|
| Open | 570-578 | `M6 17l2-5h14l-3 8a2 2 0 01-2 1H4a2 2 0 01-2-2V5a2 2 0 012-2h5l2 3h7a2 2 0 012 2v4` |
| Save | 580-588 | `M19 21H5a2 2 0 01-2-2V5a2 2 0 012-2h11l5 5v11a2 2 0 01-2 2z M17 21 17 13 7 13 7 21 M7 3 7 8 15 8` |
| Print | 590-598 | `M6 9 6 2 18 2 18 9 M6 18H4a2 2 0 01-2-2v-5a2 2 0 012-2h16a2 2 0 012 2v5a2 2 0 01-2 2h-2 M6 14h12v8H6z` |
| Email | 600-608 | `M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z M22 6 12 13 2 6` |
| Copy | 613-621 | `M9 9h13a2 2 0 012 2v9a2 2 0 01-2 2H9V9z M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1` |
| Move | 623-631 | `M5 9 2 12 5 15 M9 5 12 2 15 5 M15 19 12 22 9 19 M19 9 22 12 19 15 M2 12h20 M12 2v20` |
| Delete | 633-641 | `M3 6 5 6 21 6 M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2` |
| Rename | 643-651 | `M18 2 22 6 M7.5 20.5 19 9l-4-4L3.5 16.5 2 22z` |
| Rotate | 656-664 | `M21 2v6h-6 M21 13a9 9 0 11-3-7.7L21 8` |
| Crop | 666-674 | `M6.13 1 6 16a2 2 0 002 2h15 M1 6.13 16 6a2 2 0 012 2v15` |
| Draw | 676-686 | `M12 19l7-7 3 3-7 7-3-3z M18 13l-1.5-7.5L2 2l3.5 14.5L13 18l5-5z M2 2l7.586 7.586` + circle r=2.5 |
| Compare | 688-696 | `M3 3h18a2 2 0 012 2v14a2 2 0 01-2 2H3a2 2 0 01-2-2V5a2 2 0 012-2z M12 3v18` |
| Convert | 701-709 | `M17 2l4 4-4 4 M3 11v-1a4 4 0 014-4h14 M7 22l-4-4 4-4 M21 13v1a4 4 0 01-4 4H3` |
| Batch Convert | 711-719 | Same as Convert |
| Slideshow | 721-729 | `M5 3l14 9-14 9V3z` |
| Capture | 731-741 | `M14.5 4h-5L7 7H4a2 2 0 00-2 2v9a2 2 0 002 2h16a2 2 0 002-2V9a2 2 0 00-2-2h-3l-2.5-3z` + circle r=3 |
| Back | 746-754 | `M15 18 9 12 15 6` |
| Up | 756-764 | `M18 15 12 9 6 15` |
| Refresh | 766-774 | `M21 2v6h-6 M3 12a9 9 0 0115-6.7L21 8 M3 22v-6h6 M21 12A9 9 0 016 18.7L3 16` |
| Search | 787-795 | Circle r=7.5 + `M21 21 16.65 16.65` |
| Preview Toggle | 805-814 | `M3 3h18a2 2 0 012 2v14a2 2 0 01-2 2H3a2 2 0 01-2-2V5a2 2 0 012-2z M9 3v18` |
| Settings | 816-826 | Gear icon with center circle |

### Viewer Controls (Viewbox 14x14, Canvas 24x24)

All viewer icons use Stroke White instead of TextSecondaryBrush.

| Icon | Line | Description |
|---|---|---|
| Previous | 1564-1572 | Left chevron |
| Next | 1578-1586 | Right chevron |
| Zoom Out | 1592-1602 | Circle + minus |
| Zoom In | 1608-1618 | Circle + plus |
| Fit to Window | 1619-1628 | Four corner brackets |
| Rotate Right | 1629-1638 | Same as toolbar rotate |
| Clipping Warning | 1639-1648 | Triangle alert + exclamation |

### Window Controls (Segoe MDL2 Assets font)

| Glyph | Unicode | Line | Hover Color |
|---|---|---|---|
| Minimize | `&#xE949;` | 521 | `#30363D` |
| Maximize | `&#xE739;` | 522 | `#30363D` |
| Close | `&#xE106;` | 523 | `#E81123` (red) |

### Menu Item Icons (Segoe MDL2 Assets, 14px)

Used in context menu items (right-click on thumbnails):

| Glyph | Unicode | Action |
|---|---|---|
| Open | `&#xE8E7;` | Open file |
| Copy | `&#xE8C8;` | Copy |
| Cut | `&#xE8C6;` | Cut |
| Paste | `&#xE77F;` | Paste (disabled) |
| Delete | `&#xE74D;` | Delete |
| Rename | `&#xE8AC;` | Rename |
| Properties | `&#xE946;` | Properties |

### Color Palette

| Name | Hex | Usage |
|---|---|---|
| Background | `#0E1117` | Main background |
| Background Alt | `#161B22` | Title bar, panels, header |
| Background Canvas | `#0D1117` | Canvas area |
| Background Instruct | `#111827` | Instruction panels |
| Border | `#21262D` | Separators, dividers |
| Text Primary | `#E6EDF3` | Main text |
| Text Secondary | `#8B949E` | Icons, labels, secondary text |
| Text Muted | `#6E7681` | Subtle text, placeholders |
| Accent Green | `#238636` | Active states, confirm actions |
| Accent Blue | `#1F6FEB` | Button active/pressed |
| Button Background | `#21262D` | Button default bg |
| Button Hover | `#30363D` | Button hover bg |
| Gutter | `#161B22` | Folder tree gutter |
| Folder Orange | `#FFA000` | Folder tab icon |
| Folder Yellow | `#FFCA28` | Folder body icon |
| Folder Light Yellow | `#FFD54F` | Folder open inside |
| Folder Thumb BG | `#2A2000` | Folder thumbnail bg |
| Folder Border | `#C89400` | Folder thumbnail border, selected thumb |
| Error Red | `#DA3633` | Error badge |
| Close Hover | `#E81123` | Window close button hover |
| Preview BG | `#FAFBFC` | Preview pane background (light) |

### Design System

- **Style**: GitHub Dark / Fluent UI hybrid
- **All icons are vector** -- zero raster images in the project
- **Icon pattern**: `Viewbox` wrapping `Canvas` with `Path` data for perfect scaling
- **Toolbar buttons**: 34x34px, corner radius 6px, 1px margin
- **Viewer buttons**: 40x40px, corner radius 8px
- **Window buttons**: 46x32px
- **Stroke width**: 1.7px uniform across all vector icons
- **Corner radius convention**: 6px (buttons), 8px (viewer), 4px (badges/tags), 8px (search box)

---

## Knowledge Base

### 1. EXIF Metadata in Image Files

EXIF metadata is stored in multiple locations within a JPEG file:

| IFD (Image File Directory) | Data |
|---|---|
| **IFD0** (main) | Make, Model, DateTime, ImageDescription, Artist, Copyright |
| **Exif IFD** (sub) | DateTimeOriginal, FocalLength, FNumber, ExposureTime, ISOSpeedRatings, LensModel |
| **XMP** | Title, Description, Subject (keywords), Creator -- uses `XMP-dc:` namespace |
| **IPTC** | ObjectName (title), Caption-Abstract (description), Keywords, By-line (artist) |
| **Windows XP** (tag 0x9C9B-0x9C9F) | Title, Comment, Author, Keywords -- stored as **UTF-16LE (UCS-2)** |

### 2. Windows XP Tags Encoding Problem

Windows XP tags (tag ID 40091-40095) are stored as **UCS-2 / UTF-16LE**, not ASCII.

- `ExifLibNet.Updated`'s `ExifAscii` cannot read them (expects ASCII)
- `MetadataExtractor`'s `GetString()` cannot read them (expects UTF-8)
- Must use `GetByteArray(tagId)` then decode with `Encoding.Unicode.GetString()`

```csharp
// Reading Windows XP tags with MetadataExtractor
var bytes = ifd0.GetByteArray(40091); // XPTitle
var title = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
```

**However:** Phone camera photos (Xiaomi, Samsung, iPhone) typically have **no** Windows XP tags at all -- they only write standard EXIF tags (Make, Model, DateTimeOriginal, etc.)

### 3. Why ExifTool Instead of .NET Libraries

| | MetadataExtractor / ExifLibNet | ExifTool |
|---|---|---|
| **Encoding** | UCS-2/UTF-16LE problems | Handles automatically |
| **Format support** | Limited | Nearly all formats |
| **Write support** | Limited (ExifLibNet only) | All tags |
| **XMP/IPTC** | Manual handling | Built-in |
| **Maintenance** | NuGet updates needed | Stable binary |

### 4. UTF-8 Argument File Pattern

ExifTool on Windows has encoding issues when receiving arguments via command line because Windows recodes through the active code page, corrupting Thai/Chinese/etc. text.

**Solution:** Write arguments to a UTF-8 `.args` file, then pass to ExifTool via `-@` flag.

```csharp
await File.WriteAllLinesAsync(argumentFile, arguments,
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

process.StartInfo.ArgumentList.Add("-charset");
process.StartInfo.ArgumentList.Add("filename=UTF8");
process.StartInfo.ArgumentList.Add("-@");
process.StartInfo.ArgumentList.Add(argumentFile);
```

### 5. Async/Await in WPF

**Problem:** Using `.GetAwaiter().GetResult()` on WPF UI thread causes **deadlock** -- the app freezes.

**Cause:** WPF has a `SynchronizationContext` that posts continuations back to the UI thread, but the UI thread is blocked waiting -- no one can pump the continuation -- deadlock.

**Solution:** Use `async/await` everywhere.

```csharp
// BAD -- deadlocks
var result = ExifToolRunner.RunAsync(...).GetAwaiter().GetResult();

// GOOD -- no deadlock
var result = await ExifToolRunner.RunAsync(...);
```

### 6. ExifTool Binary Lifecycle

- **Discovery:** Search 2 locations -- `{AppDir}/tools/` and `%LOCALAPPDATA%/WpfApp1/tools/`
- **Download:** From SourceForge with manual redirect validation (max 8 hops)
- **Verification:** SHA-256 hash must match pinned hash
- **Atomic Install:** Create stage directory, extract, verify, swap directory (with backup/rollback)
- **Runtime Check:** Validate manifest, version, file inventory, support files

---

## Problem Solving Log

### Problem 1: NuGet namespace conflict

**Issue:** `MetadataExtractor` has a `Directory` class conflicting with `System.IO.Directory`

**Fix:** `using Directory = System.IO.Directory;`

### Problem 2: ExifLibNet type names wrong

**Issue:** Used `ExifString` but actual name is `ExifAscii`. `UFraction32` needs cast from `double`.

**Fix:** Read the API docs before using.

### Problem 3: Metadata not showing

**Issue:** Test images (Facebook, screenshots) had no EXIF data at all.

**Fix:** Added debug view to enumerate all tags in the image.

### Problem 4: `ContainsTag()` fails with XP tags

**Issue:** `MetadataExtractor`'s `ContainsTag(int)` does not recognize Windows XP tag IDs even when the tags exist.

**Fix:** Remove the `ContainsTag` guard, use `GetByteArray()` directly.

### Problem 5: WPF app freezes when selecting images

**Issue:** `ReadMetadataWithExifTool()` used `.GetAwaiter().GetResult()` causing deadlock on UI thread.

**Fix:** Converted all call chains to `async/await`.

### Problem 6: Bad ExifTool download UX

**Issue:** Required user to click "Click to Download", show MessageBox, wait.

**Fix:** Changed to silent auto-download on app startup.

---

## Known Issues / Unresolved

### 1. ExifTool not tested end-to-end

Code builds but has **not been run** with ExifTool installed. Need to verify JSON parsing and auto-download work correctly.

### 2. ExifTool binary ~10MB

Not bundled with app -- downloaded on first run. No metadata support without internet.

### 3. Metadata write not tested

`MetadataDialog.Save_Click()` uses ExifTool arguments but write success has not been verified. Need round-trip test: write, read back, compare.

### 4. No HEIC/HEIF preview

iPhone HEIC photos -- ExifTool supports them but WPF cannot display preview.

### 5. Thread safety of `_exifToolPath`

Set from `async void` method (`EnsureExifToolAsync`). If user selects images before download completes, will see null. No notification mechanism when download finishes.

### 6. No cancellation support

Rapid image clicks spawn multiple ExifTool processes. No `CancellationToken` to cancel stale requests.

### 7. Nullable warnings

11 CS8632 warnings. `<Nullable>enable</Nullable>` not added to csproj.

---

## Usage

1. Clone repo
2. `dotnet run`
3. App auto-downloads ExifTool on first run (~10 MB, one-time)
4. Select image -- EXIF Info tab + Metadata tab display data
5. Click "Manage Metadata..." to edit, then Save

## Dependencies

- .NET 8 SDK
- WPF (included in .NET 8 SDK)
- ExifTool v13.59 (auto-downloaded at runtime)

## License

MIT
