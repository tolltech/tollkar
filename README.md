# Tollkar

Tollkar is a local karaoke application. The repository contains the existing Avalonia desktop app and an
ASP.NET Core + React web application under `src/Tollkar.Web`.

## Run the web application

Start the web application with one command. ASP.NET Core starts the Vite development server through SpaProxy:

```sh
dotnet run --project src/Tollkar.Web/Tollkar.Web.csproj --launch-profile http
```

Open `http://localhost:5074`. During development the application redirects frontend requests to Vite at
`http://localhost:5173`; Vite proxies `/api` requests back to ASP.NET Core.

`dotnet publish` runs `npm ci` and the frontend production build, then includes the resulting SPA in the publish output.

## Validate changes

Run the repository handoff gate with `./handoff.sh`.
