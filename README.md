# Tollkar

Tollkar is a local karaoke application. The repository contains the existing Avalonia desktop app and an
ASP.NET Core + React web application under `src/Tollkar.Web`.

## Run the web application

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

`dotnet publish` runs `npm ci` and the frontend production build, then includes the resulting SPA in the publish output.

## Web authentication

Identity uses its own SQLite database (`ConnectionStrings:WebDatabase`, default `tollkar-web.db`
relative to the web process working directory), never the desktop/library database. Override it with
`ConnectionStrings__WebDatabase='Data Source=/absolute/path/tollkar-web.db'` for both EF and the server.
Apply migrations explicitly during deployment; the server does not migrate an existing database on startup.
Use HTTPS in production: authentication and antiforgery cookies are Secure outside Development.
Persist ASP.NET Core Data Protection keys securely alongside the deployment so sessions survive restarts.

`POST /api/auth/register` and `/api/auth/login` accept `{ "login": "...", "password": "..." }`.
Both create a non-persistent session and return only `{ "id": "...", "login": "..." }`.
Identity normalizes login names and enforces its default password policy and login lockout.
`GET /api/auth/me` returns that same user contract or 401; `POST /api/auth/logout` clears the cookie.
Before each POST, fetch `GET /api/auth/csrf` and send its `token` in `X-CSRF-TOKEN`, retaining cookies.
Refresh this token after login/logout because it is bound to the current identity.
Validation failures use Problem Details with a stable `errors` dictionary of code-to-message arrays;
invalid credentials return 401 with `errors.InvalidCredentials`. Passwords are never included in responses.

Authorization is required by default for new API endpoints, including future library, queue and playback APIs.
Only auth, health, the API 404 handler and the SPA fallback are explicitly anonymous.
Do not apply `AllowAnonymous` to future data endpoints; derive ownership from the authenticated user ID,
not a client-supplied ID. Queue/player display the synchronized personal queue; playback controls are a later stage.
The frontend verifies `/api/auth/me` before rendering `/queue` or `/player`.

## Library and personal queues

Set `Library__DatabasePath` to the shared catalog SQLite file (default `tollkar-library.db`, relative to
the process working directory). It must be separate from the Identity database. To use an existing
desktop catalog, point this setting at its library file. Index songs through the desktop library UI;
the web API does not expose filesystem paths or filesystem indexing operations.

The library's existing versioned SQL initializer upgrades schema 3 to 4 transactionally at startup.
Existing queue entries are preserved with owner `local-desktop`, inaccessible to web users.
Use the updated desktop application with schema 4; older versions reject this newer schema.
Identity's EF schema is unchanged. Back up the catalog before upgrading; to roll back, restore the
backup and use the previous application version (personal queues created after the backup are lost).

All catalog endpoints require authentication:

- `GET /api/library/search?text=Artist&limit=100`: title/artist prefix search, or browse without text;
  limit is 1–500. Returns metadata only, never local file paths.
- `GET /api/queue`: current user's ordered queue, including `id`, `songId`, `title`, `artist`,
  zero-based `position` and `userId`.
- `POST /api/queue` with `{ "songId": "..." }`: append a library song (duplicates allowed).
- `DELETE /api/queue/{id}`: remove a queue entry.
- `POST /api/queue/{id}/move` with `{ "offset": -1 }`: move relative to its current position,
  clamping to the first/last position.

Mutations require `X-CSRF-TOKEN` obtained as described above and return 204. Missing songs return 404;
invalid input returns 400. Deleting/moving missing or foreign queue entries is a no-op returning 204,
so the API does not reveal whether another user's entry exists. Ownership always comes from the
authenticated session, never query parameters or JSON. Tests use two separate cookie sessions to
verify read/write isolation, ordering, CSRF protection, and preservation of legacy desktop queues.

## Validate changes

Run the repository handoff gate with `./handoff.sh`.

## Real-time synchronization

See [the SignalR protocol and deployment notes](docs/realtime.md) for queue events, snapshot recovery,
state versions and single-process deployment limits.
