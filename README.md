# ClipboardCollector

A Windows 11 style WPF app for collecting text copied from web-based AI chats (or anywhere
else) into one Markdown document that you can edit and save. It's the successor to
ClipboardListener.

- .NET 10 (`net10.0-windows`), WPF with the built-in Fluent theme (follows the Windows light/dark setting)
- Installed and updated with [Velopack](https://velopack.io), the same way as ClipboardListener

## Features

| | Shortcut |
| --- | --- |
| **Auto-capture** toggle: add everything copied anywhere in Windows, or pause | Ctrl+Shift+A |
| **Paste entry**: add the clipboard as a new entry at the end (works while paused) | Ctrl+Shift+V |
| **Always on top** switch | Ctrl+Shift+T |
| **Preview**: formatted Markdown beside the editor, updated as you type | Ctrl+Shift+P |
| **PDF**: export the formatted document as a PDF | Ctrl+P |
| New / Open / Save / Save as | Ctrl+N / Ctrl+O / Ctrl+S / Ctrl+Shift+S |

Everything is also in the classic **File / Edit / View / Help** menu bar, which is where
*Save as*, *Check for updates* and *About* live. The toolbar wraps onto a second row in
narrow windows.

- Each new entry is separated from the previous one by a `---` Markdown rule
- Copies made while ClipboardCollector is in front (from the editor, the preview or a dialog)
  are never captured; a duplicate update within one second (some "copy" buttons fire twice)
  is ignored
- Preview and PDF use [Markdig](https://github.com/xoofx/markdig) to turn the Markdown into
  HTML, and the Edge WebView2 runtime (built into Windows 11) to show and print it. Raw HTML
  in the text is shown as text, never run; links in the preview open in your browser. The
  preview follows the Windows theme, the PDF is always light.
- Asks before discarding unsaved changes; the title shows `●` while there are any
- Remembers the toggles, the separator choice, window position and the last folder in
  `%LocalAppData%\ClipboardCollector\settings.json`
- Plain Ctrl+V still pastes at the cursor as normal

## Development

```powershell
dotnet run --project ClipboardCollector
```

Releases are built with `ClipboardCollector\BuildRelease.ps1`, exactly as described in the
ClipboardListener README. It uploads to `https://github.com/kreaton/ClipboardCollector`,
so that repo has to exist (and be public) before the first release and before
*Check for updates* can work.
