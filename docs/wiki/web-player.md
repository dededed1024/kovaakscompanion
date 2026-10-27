# Browser replay player

A userscript adds a video icon next to scenarios that have a recorded replay on benchmark websites; the icon opens a popup over the page that plays the clip. Everything is read from disk by the userscript manager, so it works while the app is not running and nothing is sent anywhere.

## Parts

- `Core/Web/ReplayWebFiles.cs`: writes two files into `cache/web/` (both rebuilt from the archive, so they are cache):
  - `replays.js`: `window.KC_REPLAYS = [...]`, the replays that have a video (id, scenario, score, end time, video file address).
  - `replay-player.user.js`: the userscript. The folder address and the `@match` lines (one site list, `ReplayWebFiles.Sites`) are filled in when it is written.
- The app rewrites both files when it starts and whenever a replay is saved or a video is deleted.
- Settings has a four-step guide: Tampermonkey store links (Chrome, Edge), the extension switches (with an image), the "Install userscript" button (writes the script and opens it in the default browser through `DefaultBrowser`, where the userscript manager offers to install it), and the folder pick (with an image). The images are drawn illustrations in `Assets/guide-*.png`, not screenshots; redo them if the popup look changes.
- The script reads the list with `@resource` (read from disk again each time a page loads), finds scenario names in the page text and adds the icon. The popup is a shadow-root overlay with the video on the left and a date/score list on the right; the card is as tall as the video.
- `replays.js` is `window.KC_REPLAYS={dir, items}`: `dir` is the videos folder, each item's `video` is a forward-slash path inside it.
- The clip comes from a folder the user picks in the browser (`showDirectoryPicker`, read-only). The handle is kept in the page's IndexedDB (one pick per site); a saved handle needs its permission asked again after a browser restart, which happens on the next click. The userscript walks `video` inside the folder, takes the `File` and plays it from `URL.createObjectURL(file)`. A file-backed blob is read lazily, so playback starts at once. A missing file shows the folder dialog again with the expected path.
- "Continue in app" navigates to `kovaaks-companion://open?id=<replay id>&s=<video seconds>`. The app registers the scheme for the current user at start (`ProtocolHandler`); a second launch hands the link to the running instance (`App.xaml.cs`), or starts the app when it is not running, and the replay popup opens at that time (`ReplayLink`, `AppHost.OpenLink`).

## What the user has to allow

- The userscript manager must be allowed to run user scripts (Chromium browsers: the extension's "Allow user scripts" switch) and to read file addresses (the extension's "Allow access to file URLs").
- A browser with `showDirectoryPicker` (Chrome, Edge); the user picks the videos folder once per site from the popup. Tampermonkey's "Allow scripts to access local files" is not needed: its default ("Externals") already lets `@resource` read the list.

## Why it is built this way

- A web page cannot load a file address with any tag (`iframe`, `embed`, `object`, `video`, `img`, a link) or open a window on one, and a popup opened by the page cannot be sent there afterwards. A folder the user picks is readable by the page.
- Earlier versions served the replays from a loopback HTTP server (needs the app running and the browser's local network permission), opened a `file://` player page in a tab, and then read each clip with `GM_xmlhttpRequest` into a blob (needed Tampermonkey's "All local files", and read the whole clip first: a 375 MB, 70 s, 2560x1440 clip took about 19 s to start). All were dropped.
- `@resource` for the videos was ruled out: resources are listed in the script header (a new replay would need a rewritten, reinstalled script) and `GM_getResourceURL` returns a base64 data URL of the whole file.

## Testing notes

- A tab with `visibilityState` hidden never loads media; activate the tab before judging a video test.
- Headless browsers refuse prompts; use a window with a fresh profile to see real ones.
- Settings in Tampermonkey's options page change with real key presses plus the Save button; a script-fired `change` event is not saved.