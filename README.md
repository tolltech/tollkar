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
not a client-supplied ID. Queue/player currently remain UI placeholders: tests verify isolation of identity
and protected test endpoints, not library or playback data that has not been implemented yet.
The frontend verifies `/api/auth/me` before rendering `/queue` or `/player`.

## Validate changes

Run the repository handoff gate with `./handoff.sh`.
