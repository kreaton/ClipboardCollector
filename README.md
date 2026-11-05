# ClipboardCollector

Gather everything you copy into one document. ClipboardCollector is made for saving answers from AI chats in the browser, but it works with text copied from anywhere. Copy as you go, then edit the result and save it as Markdown or export it as a PDF.

## Install

**[Download the installer](https://github.com/kreaton/ClipboardCollector/releases/latest/download/ClipboardCollector-win-Setup.exe)** and run it. If your PC doesn't have the .NET 10 Desktop Runtime yet, the installer adds it.

The installer isn't code-signed, so Windows may warn you about it. To continue, choose **More info → Run anyway**.

To get a newer version later, open **Help → Check for updates**. To remove the app, use **Settings → Apps → Installed apps** in Windows.

## How to use it

1. Start ClipboardCollector. Auto-capture is on from the start.
2. Copy text anywhere in Windows. Each copy is added to the end of the document, with a line separating it from the previous entry.
3. Edit the text if you like, and turn on **Preview** to see it formatted as you type.
4. Save the document, or export it as a PDF.

> **Auto-capture adds everything you copy, including passwords.** Pause it with Ctrl+Shift+A before you copy anything you don't want in the document.

## Shortcuts

| Action | Shortcut |
| --- | --- |
| Turn auto-capture on or off | Ctrl+Shift+A |
| Add the clipboard as a new entry, even while paused | Ctrl+Shift+V |
| Keep the window on top | Ctrl+Shift+T |
| Show or hide the preview | Ctrl+Shift+P |
| Export as PDF | Ctrl+P |
| New / Open / Save / Save as | Ctrl+N / Ctrl+O / Ctrl+S / Ctrl+Shift+S |

Plain Ctrl+V still pastes at the cursor as usual. Everything is also in the **File**, **Edit**, **View** and **Help** menus.

## Good to know

- Text you copy from ClipboardCollector itself is never captured.
- If a "copy" button fires twice within a second, the text is only added once.
- The app asks before discarding unsaved changes. A `●` in the title bar means there are some.
- The app follows the Windows light or dark setting. PDFs are always light.
- Links in the preview open in your browser. HTML in the text is shown as plain text and never run.
- Your settings, window position and last folder are remembered in `%LocalAppData%\ClipboardCollector\settings.json`.
- The version is at the bottom right of the window. Hover over it, or open **Help → About**, to see the build time and commit.

---

## For developers

The app is built with WPF on .NET 10, using the built-in Fluent theme. [Markdig](https://github.com/xoofx/markdig) turns the Markdown into HTML, and the Edge WebView2 runtime that comes with Windows 11 shows and prints it. Installs and updates use [Velopack](https://velopack.io).

To run it from source, you need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
dotnet run --project ClipboardCollector
```

### Releases

Every push to `main` builds the app on GitHub Actions and publishes a new release (`.github/workflows/release.yml`). To push without releasing, add `[skip ci]` to the commit message. You can also start a release by hand from the repository's Actions tab, or run `ClipboardCollector\BuildRelease.ps1` locally. Add `-LocalOnly` to build without uploading.

The repository must stay public, because **Check for updates** reads its releases without logging in.

### Versioning

Versions look like **1.0.42**, and the full build reads like `v1.0.42 · 2026-11-03 19:30 · 30711c7` (version · build time · commit).

- **Major.Minor** is set by hand with `<MajorMinor>` in [`Directory.Build.props`](Directory.Build.props). Raise it for bigger changes.
- **The last number** is worked out by `BuildRelease.ps1`: one higher than the latest release with the same Major.Minor, or 0 for the first. It's passed to the build as `-p:BuildNumber=…`, and local builds use 0.
- **The commit** is added by the .NET SDK from git. The build time is stamped in by `Directory.Build.props`, and `ClipboardCollector/BuildInfo.cs` puts it all together.
