<div align="center">

# QuickPanel

**Microsoft Edge's sidebar, back from the dead — and better.**

A floating panel that anchors to your browser and opens your favorite web apps
(WhatsApp, Gmail, Claude, Notion…) in native windows, without breaking your flow.

[![License](https://img.shields.io/badge/license-PolyForm%20Noncommercial-blue)](LICENSE)
[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)

</div>

---

## What is this?

Microsoft removed Edge's app sidebar. QuickPanel brings it back as a native Windows app: a
floating button anchored to your browser that opens your favorite web apps in native panels
with persistent sessions.

Despite the name, it isn't limited to Edge: it works with whichever Chromium-based browser you
have set as your Windows default — **Edge, Chrome, Brave, Opera, or Vivaldi**.

Unlike browser extensions (which rely on iframes and break on Gmail, WhatsApp, etc.), QuickPanel
uses **WebView2** — the same engine as the browser itself — so **any site loads without
restrictions**.

**Free, with every feature unlocked.** No tiers, no paywalls, no account.

## Features

**Core**
- **Anchors to your browser** — detects your default Windows browser (Edge, Chrome, Brave, Opera,
  or Vivaldi) once at startup and anchors to it; one button per open window. If your default
  browser isn't Chromium-based (e.g. Firefox), the app warns you on launch and won't anchor.
- **Native apps via WebView2** — Gmail, WhatsApp, and any website load without iframe restrictions.
- **Persistent sessions and zoom** — each app keeps its login and zoom level between launches.
- **Non-disruptive by design** — minimizing a panel hides it without losing state; closing it frees
  memory; clicking outside the Settings window dismisses it on its own (unless a child dialog,
  like Manage Apps or a hotkey capture, is open).
- **External links open in your browser** — a link that points to another domain (e.g. an
  attachment in an email) opens in your actual browser instead of inside the panel. Federated
  login flows (Google, Microsoft, etc.) are excluded so sign-in isn't broken.
- **Smart layout** — the menu and panels position themselves based on which side the button is on
  and how much space is available, without overflowing the window.
- **Native popups stay on top, not closed** — file pickers, date pickers, dropdowns, and modals
  that a page renders as native popups (common in WhatsApp Web, Gmail, and Partner-style admin
  panels) used to make the panel close as if you'd clicked outside it. The panel now recognizes
  these and stays open behind them.

**Customization**
- Dark, light, or system theme, with color palettes.
- Drag-and-drop reordering, custom icons and names, per-app accent color.
- Panel and menu sizes (S/M/L).
- Groups/folders to organize apps in the menu — collapsed folders show a count badge and the
  folder name on hover; expanded apps render slightly smaller inside a pill-shaped, tinted
  background using your theme's primary color.
- Export and import your whole setup (apps, groups, theme, hotkeys) as a single file — handy for
  moving to another PC or keeping a backup.
- **Cloud sync** — link your own Google Drive or OneDrive (private app folder, invisible to you
  in the regular Drive UI) to sync settings, apps, folders, and shortcuts across PCs. Choose how
  often it syncs: manual, on app close (default), every 15 minutes, hourly, or instantly. When two
  PCs both changed something, it merges field-by-field instead of overwriting — the newest edit
  per app/folder/shortcut wins, deletions are respected on both sides, and if a brand-new PC finds
  existing cloud data it asks which version to keep before touching anything. Web sessions are
  never synced, only the config.

**Productivity**
- Global keyboard shortcuts: `Ctrl+Alt+1‑0` auto-assign to your first 10 apps (and reassign
  automatically if you reorder them); everything past that, plus global actions (toggle menu, hide
  active panel, next/previous app, toggle auto-hide, move the button), are configurable from
  Settings or Manage Apps.
- Per-app navigation history and quick search in Manage Apps.
- Auto-hide the floating button when the cursor is away (it stays fully visible while the menu is
  open, even if the cursor moves elsewhere).
- Launch a specific app automatically on startup.
- Unread notification badges per app and a total on the floating button, parsed from each app's
  page title.

**Performance**
- Shared WebView2 environment across all panels with per-app named profiles — base Chromium
  processes (GPU, network, storage, audio) are shared instead of duplicated per app, while session
  isolation between apps is preserved.
- **Lite Mode**, built for low-RAM machines: hidden panels suspend after 20 seconds (lowering their
  memory footprint immediately and freeing most of it once suspended), and at most 3 panels stay
  active at once — the oldest one is closed automatically when a new one is opened, without losing
  its configuration. Any panel can be marked **"Keep alive"** to opt out of suspension and the
  3-panel limit entirely (handy for background music or an ongoing call); pinned panels don't
  count against the limit.

## Roadmap

Things planned but not built yet — not promises, just where this is headed:

- Picture-in-picture for panels.
- Multiple accounts and multiple profiles per app.

## Installation

Pick whichever fits you:

- **Microsoft Store** — [SidePanel for Browsers](https://apps.microsoft.com/detail/9N3Z0WKL8KPN).
  Installs and updates itself like any Store app.
- **Installer** — download `QuickPanelSetup.exe` from [Releases](../../releases) and run it. No
  admin rights required. Adds a Start Menu shortcut and an uninstaller.
- **Portable** — download `QuickPanel-<version>.zip` from [Releases](../../releases), extract it
  anywhere (e.g. `C:\Tools\QuickPanel`), and run `QuickPanel.exe`. Nothing is installed.

After installing (any method), enable "Start with Windows" from Settings if you want it and
weren't asked during setup.

> **Requirement**: [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
> (preinstalled on Windows 11; installed automatically with an up-to-date Edge on Windows 10).
>
> **Browser**: QuickPanel anchors to your default Windows browser. Works with any Chromium-based
> browser (Edge, Chrome, Brave, Opera, Vivaldi). If your default is something else (e.g. Firefox),
> the app will let you know on startup and won't be able to anchor.

## Usage

- Open your browser → the floating button appears.
- Click the button → the menu expands with `+` (add app) and `⚙` (settings).
- Add apps from presets or by pasting a URL.
- Click an app → its panel opens anchored to the browser window, resizable from its free edge.
- Right-click an app in the menu → remove it.
- Drag from the handle (`⠿`) of a row in Manage Apps to reorder.

## Building from source

```powershell
git clone https://github.com/lucasgiusiano/QuickPanel.git
cd QuickPanel
dotnet publish QuickPanel.csproj -c Release -r win-x64 --self-contained true -o publish
```

The output lands in `publish\QuickPanel.exe`.

> **Cloud Sync won't compile out of the box.** `Services/CloudSync/CloudSyncConstants.cs` is
> `partial` and expects a `CloudSyncSecrets.cs` file (gitignored, not in this repo) with your own
> Google Cloud / Entra ID OAuth Client ID and Secret — see
> `Services/CloudSync/CloudSyncSecrets.example.cs` for the exact fields. In a desktop app these
> values aren't secret in the traditional sense (PKCE + local loopback redirect is what actually
> protects the flow), but they're still tied to *this* app's OAuth registration, so they're kept
> out of the public repo. Create your own OAuth client in each provider and drop your values into
> `CloudSyncSecrets.cs` to build with Cloud Sync working; everything else compiles and runs fine
> without it, Cloud Sync just won't be available.

To build an installer, install [Inno Setup](https://jrsoftware.org/isinfo.php) and compile
`installer.iss` with `ISCC.exe installer.iss` — the result lands in `installer-output\`.

## Tech stack

WPF (.NET 8) · WebView2 (shared environment across panels) · Win32 interop (`SetWinEventHook`,
`GWLP_HWNDPARENT`, `RegisterHotKey`) · Material Design 3

Browser window tracking uses Windows event hooks; each panel anchors cross-process to its
window's rect. The target browser is detected once at startup by reading Windows' default browser
setting. See `Core/` and `Services/` for implementation details.

## License

[PolyForm Noncommercial 1.0.0](LICENSE) — the source is visible and you're free to build it for
personal use, but commercial use and resale are reserved to the author.

---

<div align="center">
Made by <a href="https://lucasgiusiano.uy">Lucas Giusiano</a>
</div>
