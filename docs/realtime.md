# Real-time queue synchronization

The queue and player pages share one authenticated SignalR connection per application layout.
The queue page supports catalog search, add/remove/reorder and selecting a current song.
Both pages display the current selection; the player streams and synchronizes HTML5 video.
The existing HTTP queue API and CSRF requirements are unchanged.

## Protocol

- Connect to `/api/karaoke` using the Identity session cookie. Anonymous negotiation returns 401.
- The server joins the connection to `karaoke:{UserId}` using the authenticated name-identifier
  claim. Clients cannot select a user or join groups themselves.
- After connection/start and every reconnect, invoke `GetSnapshot` without arguments.
- `GetSnapshot` returns `{ version, items, currentItemId, playback }`; `items` has the same shape as `GET /api/queue`.
  Each item carries the song's `capabilities` flags so a player can choose how to render it without a
  further request: a video song plays in one element, a karaoke song plays its track under its own
  timed lyrics.
- `QueueChanged` sends that full snapshot to the affected user's connections after an HTTP queue
  mutation. Versions can have gaps; full snapshots mean no delta replay is needed.
- `QueueInvalidated` has no arguments or user data. A library refresh broadcasts it to authenticated
  connections because metadata updates or cascading song deletions can affect any queue. Clients
  then invoke `GetSnapshot`. Interrupted scans also invalidate potentially committed changes.

Register event handlers before starting the connection. Accept only versions greater than the last
accepted version within the current connection generation. Events may arrive before the snapshot
response or out of order; the newer version wins. Clear the version watermark on reconnect, and ignore
snapshot responses from earlier generations. This also permits recovery after a server restart.

The browser retries initial connection failures and reconnects every two seconds. It shows a recovery
status until a snapshot is fetched, retains the previous display during an outage, and stops the
connection and timers when the authenticated layout unmounts. A snapshot refresh every 30 seconds
also repairs isolated failed publications without requiring a transport failure.

## Server guarantees and deployment limits

Versions are process-wide monotonic numbers kept in memory, not database revisions. Mutation,
version assignment and snapshot capture share a short lock. Publishing occurs outside that lock,
with a five-second server timeout independent of the originating HTTP request cancellation.
No-op mutation requests may also advance the version. A library scan advances the version when its
enumeration finishes or is disposed; intermediate scan results are not promised as atomic snapshots.

Run a single web server process for this stage. Multiple replicas or external writers to personal
queues require shared durable versions and cross-process notifications before they are supported.
The desktop application keeps its separate `local-desktop` queue. No database migration is required.
Use a reverse proxy with WebSocket support; the Vite `/api` development proxy enables WebSockets.

## Validation

`./handoff.sh` runs frontend lint, Node tests, production build, solution build and .NET tests.
The frontend tests use Node's native TypeScript stripping (Node 22.18+ or a newer supported release).
Web integration tests exercise the SignalR JSON protocol over in-memory TestServer WebSockets:
user isolation, multiple devices, add/move/delete events, concurrent versions, reconnect recovery,
library deletion invalidation and anonymous rejection. Frontend tests cover stale/duplicate snapshots
and responses from earlier connection generations, including a server version reset.

## Queue controls

`POST /api/queue/{id}/play` selects that queue entry as current without reordering or removing songs.
It requires authentication and CSRF like other mutations. Missing/foreign entries are no-ops; an empty
GUID is invalid. Selection is isolated per user and included in versioned snapshots on all devices.
Duplicate songs are distinguished by queue entry ID. Moving a selected entry preserves selection;
removing it (including a catalog deletion) clears selection. Selection is held in server memory and
resets on server restart, while queue ordering persists in SQLite. No migration is needed.
Selecting a song starts its playback timeline at zero. The timeline is desired state, not confirmation that every browser can play the media.

The UI debounces prefix searches by 300 ms, cancels obsolete requests and displays up to 100 results.
Mutation buttons are disabled during a request or connection recovery. Errors do not optimistically
change the queue or retry mutations (an interrupted request may already have committed).
Responsive panels stack on narrow screens, with labelled controls and touch targets of at least 44 px.
`DELETE /api/queue` removes every pending entry. A selected entry remains current so playback is not
interrupted; it is removed when playback advances or when another entry is selected.

## Playback protocol

`playback` is null without a selection, otherwise it contains `revision`, `isPlaying` and
`positionSeconds` sampled when the snapshot is captured. The server measures elapsed time with
TimeProvider's monotonic clock. Browsers anchor this position to their own `performance.now()` on
receipt, avoiding dependence on device clock settings; network delivery latency remains an
approximation. Drift greater than 750 ms is corrected every second. Buffering does not stop the
shared timeline, so a recovering player catches up.

`POST /api/queue/playback` accepts `{ action, revision, positionSeconds? }` with authentication and
CSRF protection. Actions are `play`, `pause`, `seek`, `next`, and `ended`. Seek accepts finite
positions from zero to 86400 seconds; the browser clamps to the actual media duration. Commands
apply only to the authenticated user's current playback revision. Every accepted playback change
creates a new revision; stale commands are no-ops. Queue edits do not change playback revisions.
This prevents two players reporting completion, or a delayed command, from skipping a song.
Missing selection and an `ended` command while paused are no-ops. Next follows current queue order;
at its end selection and playback are normally cleared while entries remain. If queue clearing retained
the current entry, advancing also removes that retained entry.

A karaoke song adds two layers that read the same timeline instead of driving it. The backdrop clip
is a muted element corrected against the shared position like the main one, wrapping around when it
loops and holding its last frame when it does not. The lyrics follow the media element's own
`currentTime` on every animation frame, because syllables turn over faster than `timeupdate` fires;
they publish no commands and cannot affect other devices.

Both the media ended event and periodic duration checks request automatic advancement. Duration
checks recover a skipped completion during another command or a reload past the end. Only this
revision-guarded completion is retried automatically; manual mutations retain the existing no-retry
behavior. Unsupported or missing media displays an error and allows manual next without skipping
songs silently. Sound activation and fullscreen do not broadcast commands.

Tests cover deterministic timeline progression, pause/seek/reload, duplicate completion, stale
commands, queue exhaustion, authenticated multi-device events, isolation, CSRF and validation.
Frontend tests cover position interpolation and advancement after reload/overlapping commands.
Media errors, including decode failures after metadata was loaded, prevent timer-based advancement.
The integration suite also races completion from two players, verifies only one transition, and
recovers an exhausted queue after disconnect. See [the acceptance checklist](web-workflow.md)
for browser checks and launch/publication instructions.
