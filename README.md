# Simple Watermark & Collage

A small Windows app for two things:

- **Watermarks**: put a picture or text mark on many pictures at once.
- **Collages**: arrange up to 10 pictures on one page, with 10 templates to choose from.

It is a single `.exe` with no installer. It only needs the .NET Framework 4.x that comes with Windows 10 and 11.

## Download

Get `Watermark.exe` from the [Releases](../../releases) page and run it. Settings are saved in `settings.ini` next to the exe.

## Watermark tab

- **Image mark**: any picture you choose (a transparent PNG works best).
- **Text mark**: any installed font, with bold/italic, colour, outline (colour and thickness) and shadow.
- **Placement**: 7 positions, plus size, opacity, margin, angle (-45° to 45°) and JPG quality.
- **Live preview** of the selected picture.

## Collage tab

- **Up to 10 pictures**: the ones selected in the list, or else the first 10. Change the order with ▲ Up / ▼ Down; the first picture gets the big spot.
- **10 templates**: Grid, Rows, Columns, Big top, Big left, Spotlight, Two + row, Bricks, Spiral, Magazine.
- **Spacing and shape**: spacing between pictures, outer border, rounded corners and background colour.
- **Size**: format 1:1, 4:5, 9:16, 3:2 or 16:9, and width from 1080 to 3000 px.
- **More options**: keep whole pictures (no cropping), add the watermark on top, save as JPG or PNG.

## Your originals are safe

- Everything is saved to a folder you choose.
- The pictures' own folder is refused for watermarks.
- Files that are already there are never replaced: the new one is saved as `name (2).jpg`, `name (3).jpg`, and so on.

Pictures can be added with the buttons, by dropping files or folders on the window, or by dropping them on `Watermark.exe`. The supported formats are JPG, PNG, BMP, GIF and TIFF.

## Build from source

You don't need Visual Studio. Run:

```
src\build.bat
```

It compiles `src\Watermark.cs` (C# 5, WinForms) with the `csc.exe` that ships with Windows (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319`) and writes `Watermark.exe` next to the `src` folder.
