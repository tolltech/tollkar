# Tollkar

Tollkar is a local karaoke application. The repository contains the existing Avalonia desktop app and an
ASP.NET Core + React web application under `src/Tollkar.Web`.

## Run the web application

See [the launch, publication and end-to-end acceptance guide](docs/web-workflow.md)
for deployment commands, prerequisites and multi-tab recovery scenarios.

Restore the pinned EF tool and apply the Identity migration before the first run:

```sh
dotnet tool restore
dotnet ef database update --project src/Tollkar.Web --startup-project src/Tollkar.Web --context WebDbContext
```

Start the web application. ASP.NET Core starts the Vite development server through SpaProxy:

```sh
dotnet run --project src/Tollkar.Web/Tollkar.Web.csproj --launch-profile http
```

Open `http://localhost:5074`. During development the application redirects frontend requests to Vite at
`http://localhost:5173`; Vite proxies `/api` requests back to ASP.NET Core.

Use `./publish.sh /path/to/deployment` on macOS to publish the web application and create or migrate
both databases without overwriting songs, logs or existing server configuration. Back up persistent
data first; the script starts or restarts the required `local.tollkar.web` LaunchAgent automatically.
Use `./publish-files.sh /path/to/deployment` to build and copy the same application files without
touching LaunchAgent state, running database migrations or checking server health. A running process
continues to use its loaded binaries until it is restarted manually. The local target wrappers are
`./publish-server.sh` and `./publish-files-server.sh` respectively.
See the deployment guide above for Caddy and runtime configuration.
`dotnet publish` runs `npm ci` and the frontend production build, then includes the resulting SPA in the publish output.
The web server writes application and ASP.NET Core events through Vostok.Logging with the base path
`logs/web.log`, rotating at 100 MB and retaining five numerically suffixed log parts.

## Web authentication

Identity uses its own SQLite database (`ConnectionStrings:WebDatabase`, default `tollkar-web.db`
relative to the web process working directory), never the desktop/library database. Override it with
`ConnectionStrings__WebDatabase='Data Source=/absolute/path/tollkar-web.db'` for both EF and the server.
Apply migrations explicitly during deployment; the server does not migrate an existing database on startup.
Use HTTPS in production: authentication and antiforgery cookies are Secure outside Development.
Persist ASP.NET Core Data Protection keys securely alongside the deployment so sessions survive restarts.

`POST /api/auth/login` accepts `{ "login": "...", "password": "..." }`, creates a non-persistent
session and returns `{ "id": "...", "login": "...", "isAdmin": true|false }`.
Public registration is disabled. An existing user whose normalized login is `admin` can create users
from `/admin`; the protected `POST /api/auth/register` endpoint accepts the same credentials and does
not replace the administrator's current session. The initial `admin` account must be provisioned separately
before registration is locked down.
Identity normalizes login names and enforces its default password policy and login lockout.
`GET /api/auth/me` returns the same user contract or 401; `POST /api/auth/logout` clears the cookie.
Before each POST, fetch `GET /api/auth/csrf` and send its `token` in `X-CSRF-TOKEN`, retaining cookies.
Refresh this token after login/logout because it is bound to the current identity.
Validation failures use Problem Details with a stable `errors` dictionary of code-to-message arrays;
invalid credentials return 401 with `errors.InvalidCredentials`. Passwords are never included in responses.

Authorization is required by default for new API endpoints, including future library, queue and playback APIs.
Only auth, health, the API 404 handler and the SPA fallback are explicitly anonymous.
Do not apply `AllowAnonymous` to future data endpoints; derive ownership from the authenticated user ID,
not a client-supplied ID. The queue page provides search and queue controls; both pages display the synchronized current song.
The player streams HTML5 video with synchronized playback controls.
The frontend verifies `/api/auth/me` before rendering `/queue`, `/player` or the admin-only `/admin` page.

An authenticated account sees a guest QR code on the queue page and can hide or reveal it. The code opens
the same owner's queue and player without asking the visitor to log in. Guest sessions can search, edit the
shared queue and control playback, but cannot open administration or issue another guest code. The signed
link and the resulting guest session expire at the next calendar date in the server's local time; a queue
page left open refreshes the QR code at that boundary. Persist Data Protection keys in production because
they are also used to validate guest links.

## Library and personal queues

Set `Library__DatabasePath` to the shared catalog SQLite file (default `tollkar-library.db`, relative to
the process working directory). It must be separate from the Identity database. To use an existing
desktop catalog, point this setting at its library file.

For a separate web catalog, manually put songs in `src/Tollkar.Web/songs` during development,
or `songs` under the deployed application's content root. The directory is created automatically.
A hosted background service scans it at startup, then waits 30 seconds after each completed scan
before scanning again. Subdirectories are included; added and changed files are indexed, unchanged
files keep their IDs, and deleted files are removed from the catalog using the same scanner as desktop.
Only this directory is refreshed automatically; any other catalog roots remain untouched.
Scan failures are logged and retried on the next pass without stopping the web server.

Songs found in a subdirectory are labelled in search results with the name of the first folder under
the songs root, so the same title from different sources stays distinguishable; songs kept directly in
the root get no label, and deeper nesting still shows only that first folder.

Supported files are `.mp4` and `.kfn`. Use `Artist - Title.mp4` for video metadata. A KFN container
carries its own title and artist, falling back to the file name and the containing folder, so filing
them as `Artist/Title.kfn` is enough; see [the KFN notes](docs/kfn-format.md).
To avoid indexing a partially copied file, copy it with a temporary extension and rename it to the
final one when the transfer completes. No desktop application or server restart is needed for new
songs. The desktop application indexes KFN containers but cannot play them; only the web player can.
The queue page supports song search, adding, removing, moving up/down and selecting a current song.
Selection and playback are synchronized across devices.

Configure `Library:SongsPath` (environment variable `Library__SongsPath`) to change the directory;
relative paths resolve against the web application's content root, not the shell working directory.
Configure `Library:SyncInterval` (`Library__SyncInterval`, for example `00:01:00`) to change the delay;
it must be positive and no greater than one day. Keep the songs directory outside `wwwroot`:
it is server-side storage and must not be publicly served as static files.
Songs are ignored by Git and excluded from build/publish output; provision or persist the directory
separately during deployment and grant the server read/write access to it and the catalog database.
The Identity database remains separate; the web API does not expose filesystem paths or indexing
operations — search results carry only the first folder name as a label, never a path.

The library's existing versioned SQL initializer upgrades schema 3 to 4 transactionally at startup.
Existing queue entries are preserved with owner `local-desktop`, inaccessible to web users.
Use the updated desktop application with schema 4; older versions reject this newer schema.
Identity's EF schema is unchanged. Back up the catalog before upgrading; to roll back, restore the
backup and use the previous application version (personal queues created after the backup are lost).

All catalog endpoints require authentication:

- `GET /api/library/search?text=Artist&limit=100`: title/artist prefix search, or browse without text;
  limit is 1–500. Results are sorted by play count descending, then folder, artist and title.
  Each result includes the play count; responses contain metadata only, never local file paths.
- `GET /api/queue`: current user's ordered queue, including `id`, `songId`, `title`, `artist`,
  `capabilities`, zero-based `position` and `userId`. `capabilities` is a flags value describing what
  the song offers (audio, video, synchronized lyrics); the player picks its mode from it.
- `POST /api/queue` with `{ "songId": "..." }`: append a library song (duplicates allowed).
- `DELETE /api/queue`: clear the queue. If a song is currently playing, it remains until playback
  advances or another song is selected, then it is removed.
- `DELETE /api/queue/{id}`: remove a queue entry.
- `POST /api/queue/{id}/move` with `{ "offset": -1 }`: move relative to its current position,
  clamping to the first/last position.
- `POST /api/queue/{id}/play`: select the current queue entry without changing its position.
  Starts the selected song from zero on connected players. Selection resets on server restart and clears when the entry is removed.

Mutations require `X-CSRF-TOKEN` obtained as described above and return 204. Missing songs return 404;
invalid input returns 400. Deleting/moving missing or foreign queue entries is a no-op returning 204,
so the API does not reveal whether another user's entry exists. Ownership always comes from the
authenticated session, never query parameters or JSON. Tests use two separate cookie sessions to
verify read/write isolation, ordering, CSRF protection, and preservation of legacy desktop queues.

## Validate changes

Run the repository handoff gate with `./handoff.sh`.

## Streaming songs

`GET /api/songs/{songId}/media` streams an indexed song to an authenticated browser using its
session cookie. `HEAD` returns the same media headers without a body. Use the song ID from library
search or a queue item; the API accepts no filesystem path. All signed-in users can stream catalog
songs; a song does not need to belong to their queue. A video song is served as `video/mp4`; a
karaoke container is served as the `audio/mpeg` track it holds, read straight out of the container
without extracting it to disk.

Responses use `X-Content-Type-Options: nosniff` and `Cache-Control: no-store`.
ASP.NET Core processes byte ranges directly from a seekable file stream without buffering the whole
video: full requests return 200, satisfiable single ranges (including suffix/open-ended ranges) return
206 with `Content-Range`, and unsatisfiable ranges return 416 with the file length. Malformed and
multiple ranges fall back to the full response. Unauthenticated requests return 401 without redirecting
to login, including HEAD and Range requests.

Two further endpoints serve karaoke songs and return 404 for anything else.
`GET|HEAD /api/songs/{songId}/background` streams the backdrop clip as `video/mp4`, and only when the
container really holds an MP4; clips a browser cannot play are reported as absent instead.
`GET /api/songs/{songId}/karaoke` returns `{ background, lines }`, where `background` is `null` or
`{ loop }` and each line carries its syllables with start and end times in milliseconds.

Only files under the configured `Library:SongsPath` are served. Catalog entries from other roots,
unknown IDs, missing/unreadable files, unsupported formats, damaged containers, and symbolic links in
the songs directory return 404 without exposing local paths.
The songs root itself must not be a symbolic link.
Keep this directory outside `wwwroot` and do not expose it through a reverse proxy's static-file route.
The directory, its ancestors and catalog database must be writable only by trusted server operators:
path checks are not a sandbox against a local process replacing filesystem entries concurrently.
The player UI uses this endpoint directly with the browser session cookie.

## Real-time synchronization

See [the SignalR protocol and deployment notes](docs/realtime.md) for queue events, snapshot recovery,
state versions and single-process deployment limits.

## Web player

Open `/player` after selecting a song in the queue. A video song fills the stage; a karaoke song
plays its track over the backdrop clip, if it has one, with the lyrics drawn on top and highlighted
syllable by syllable as they are sung. The backdrop is muted and follows the shared timeline,
restarting or holding its last frame when it is shorter than the song.
HTML5 media starts muted; the first pointer or keyboard action enables audio.
If the browser requires an explicit playback gesture, use the displayed activation button.
Audio permission is local to each page and must be enabled again after reload.
Play/pause, next and seeking update all connected players for the same user. Fullscreen and sound
activation are local controls; fullscreen requires browser support. MP4 codecs must be supported by
the browser (a playable container alone does not guarantee codec support).

The server keeps a monotonic playback timeline; reloading or reconnecting restores the selected song,
position and play/pause state. During connection recovery the browser pauses until a fresh snapshot
arrives. An active player advances to the next queue entry at the end; the last entry normally clears
selection without deleting the queue. A current entry retained by clearing the queue is removed when
playback advances. If all players are closed, advancement waits until a player returns.
Server restart resets playback; persisted queue entries remain. See [the protocol](docs/realtime.md).
