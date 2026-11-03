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
| **Always on top** (pin button) | Ctrl+Shift+T |
| New / Open / Save / Save as | Ctrl+N / Ctrl+O / Ctrl+S / Ctrl+Shift+S |

- Entries are separated by a blank line, or by a `---` rule (*More* menu)
- Text copied from the editor itself is never captured again; a duplicate update within one
  second (some "copy" buttons fire twice) is ignored
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
