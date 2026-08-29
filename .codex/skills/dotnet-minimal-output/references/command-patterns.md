# Dotnet Command Patterns

Use these patterns when the wrapped command would otherwise produce noisy output.

## Restore

```sh
sh .codex/skills/dotnet-minimal-output/scripts/dotnet-quiet.sh \
  --label restore-solution \
  -- dotnet restore MySolution.sln --nologo --verbosity minimal
```

## Build

```sh
sh .codex/skills/dotnet-minimal-output/scripts/dotnet-quiet.sh \
  --label build-api \
  -- dotnet build src/Api/Api.csproj --nologo --verbosity minimal
```

## Test

```sh
sh .codex/skills/dotnet-minimal-output/scripts/dotnet-quiet.sh \
  --label test-unit \
  -- dotnet test tests/UnitTests/UnitTests.csproj --nologo --verbosity minimal
```

## Run

```sh
sh .codex/skills/dotnet-minimal-output/scripts/dotnet-quiet.sh \
  --label run-worker \
  -- dotnet run --project src/Worker/Worker.csproj --no-build
```

## Format

```sh
sh .codex/skills/dotnet-minimal-output/scripts/dotnet-quiet.sh \
  --label format-solution \
  -- dotnet format MySolution.sln --verbosity minimal
```

## Publish

```sh
sh .codex/skills/dotnet-minimal-output/scripts/dotnet-quiet.sh \
  --label publish-api \
  -- dotnet publish src/Api/Api.csproj --nologo --verbosity minimal
```

## Inspect

```sh
sh .codex/skills/dotnet-minimal-output/scripts/dotnet-quiet.sh \
  --label list-packages \
  -- dotnet list src/Api/Api.csproj package
```

## Retry With More Detail

Increase native verbosity only after an initial failure, and keep logging to `_tmp/`.

```sh
sh .codex/skills/dotnet-minimal-output/scripts/dotnet-quiet.sh \
  --label test-unit \
  -- dotnet test tests/UnitTests/UnitTests.csproj --nologo --verbosity normal
```
