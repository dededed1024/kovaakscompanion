# Browser replay player

A userscript adds a video icon next to scenarios that have a recorded replay on benchmark websites; the icon opens a popup over the page that plays the clip. Everything is read from disk by the userscript manager, so it works while the app is not running and nothing is sent anywhere.

## Parts

- `Core/Web/ReplayWebFiles.cs`: writes two files into `cache/web/` (both rebuilt from the archive, so they are cache):
  - `replays.js`: `window.KC_REPLAYS = [...]`, the replays that have a video (id, scenario, score, end time, video file address).
  - `replay-player.user.js`: the userscript. The folder address and the `@match` lines (one site list, `ReplayWebFiles.Sites`) are filled in when it is written.
- The app rewrites the list when it starts and whenever a replay is saved or a video is deleted.
- Settings has an "Install userscript" button: it writes the script and opens it in the default browser (`DefaultBrowser`), where the userscript manager offers to install it.
- The script reads the list with `@resource` (read from disk again each time a page loads), finds scenario names in the page text and adds the icon. The popup is a shadow-root overlay with the video on the left and a date/score list on the right; the card is as tall as the video.
- The clip is read from disk with `GM_xmlhttpRequest` (`responseType: blob`) and played from a blob URL. The popup shows "Loading the video..." until it is in.
- "Continue in app" navigates to `kovaaks-companion://open?id=<replay id>&s=<video seconds>`. The app registers the scheme for the current user at start (`ProtocolHandler`); a second launch hands the link to the running instance (`App.xaml.cs`), or starts the app when it is not running, and the replay popup opens at that time (`ReplayLink`, `AppHost.OpenLink`).

## What the user has to allow

- The userscript manager must be allowed to run user scripts (Chromium browsers: the extension's "Allow user scripts" switch) and to read file addresses (the extension's "Allow access to file URLs").
- Tampermonkey: Settings > Config mode: Advanced, then Security > "Allow scripts to access local files": All local files, and the Save button below that section. The default ("Externals") lets `@resource` read the list but refuses `GM_xmlhttpRequest` on a file address ("Access to this local file is forbidden!"). A change made without pressing Save is not kept.
- "All local files" lets every installed userscript read local files; say so when asking for it.

## Why it is built this way

- A web page cannot load a file address with any tag (`iframe`, `embed`, `object`, `video`, `img`, a link) or open a window on one, and a popup opened by the page cannot be sent there afterwards. Only the userscript manager can read the file, so the clip goes through it into memory.
- The cost: the whole clip is read before it plays (a 375 MB, 70 s, 2560x1440 clip took about 7 s to read and about 19 s to start in a test), and it is held in memory while the popup is open. Longer or higher-bitrate clips wait longer.
- Earlier versions served the replays from a loopback HTTP server (needs the app running and the browser's local network permission) and then opened a `file://` player page in a tab (a tab only, since a page cannot open a sized window on a file address). Both were dropped.

## Testing notes

- A tab with `visibilityState` hidden never loads media; activate the tab before judging a video test.
- Headless browsers refuse prompts; use a window with a fresh profile to see real ones.
- Settings in Tampermonkey's options page change with real key presses plus the Save button; a script-fired `change` event is not saved.