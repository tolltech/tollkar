# Real-time queue synchronization

The queue and player pages share one authenticated SignalR connection per application layout.
They display the current queue; search controls and playback commands are separate stages.
The existing HTTP queue API and CSRF requirements are unchanged.

## Protocol

- Connect to `/api/karaoke` using the Identity session cookie. Anonymous negotiation returns 401.
- The server joins the connection to `karaoke:{UserId}` using the authenticated name-identifier
  claim. Clients cannot select a user or join groups themselves.
- After connection/start and every reconnect, invoke `GetSnapshot` without arguments.
- `GetSnapshot` returns `{ version, items }`; `items` has the same shape as `GET /api/queue`.
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
