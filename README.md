# Simple Watermark & Collage

A small Windows app for three things:

- **Watermarks**: put a picture or text mark on many pictures at once.
- **Collages**: arrange up to 10 pictures on one page, with 10 templates to choose from.
- **Rename**: rename or copy many files at once, with a pattern or with a name you type for each one.

It is a single `.exe` with no installer. It only needs the .NET Framework 4.x that comes with Windows 10 and 11.

The window has a side menu for the three tools, a toolbar with the main actions of each page, a status bar, and slim loading bars for long jobs. It opens with the size and position it was left with.

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

## Rename tab

- **A small file browser**: folders on the left (Desktop, Downloads, Pictures, D:\Comics, every drive); in the middle every folder, picture and file of the open folder, as a list or as big thumbnails, with a tick box on each. Search, show only pictures / files / folders, subfolders too, sort by name, type, size or date.
- **1 Choose what**: tick all, none or only the pictures (or tick by hand). The ticked ones are numbered in the list order.
- **2 New names**: a pattern with `{name}` `{n}` `{folder}` `{date}` (buttons insert them), ready-made names, numbering (start, step, digits), find & replace, letters (lowercase, UPPERCASE, Title Case, web-slug), spaces and lowercase extension.
- **Any name you want**: double-click a file (or F2): a window with the big picture, type the name, Enter saves it and goes to the next one.
- **3 Where**: same folder = rename (with **Undo last rename**); other folder = **copies** with the new names, the originals stay.
- Nothing is renamed while a new name is in red (two items with the same name, a file already there).
- **Big folders stay fast**: the folder is read and the thumbnails are made in the background (a loading bar shows how far it is), and the list only draws the rows on screen, so thousands of pictures open at once.

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

It compiles the C# 5 / WinForms sources with the `csc.exe` that ships with Windows (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319`) and writes `Watermark.exe` next to the `src` folder:

- `src\Watermark.cs`: the window, the Watermark and Collage tabs, settings.
- `src\Rename.cs`: the Rename tab (folders, the three steps, renaming, copying, undo).
- `src\RenameList.cs`: the Rename file list, thumbnails and the "give it a name" window.
- `src\Modern.cs`: the look (rounded buttons and cards, side menu buttons, loading bar).

The thumbnails use the Windows image decoder from WPF (`PresentationCore`), which is part of .NET Framework 4.x as well.
