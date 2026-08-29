---
name: dotnet-minimal-output
description: Run `dotnet` CLI commands with minimal terminal output while preserving full stdout and stderr logs under `_tmp/`. Use when Codex needs to build, test, restore, publish, pack, run, format, or inspect .NET projects in this repository and token economy matters, especially for commands that normally emit noisy restore, analyzer, or test output.
---

# Dotnet Minimal Output

## Start Here

- Prefer `scripts/dotnet-quiet.sh` for any `dotnet` command that may emit more than a few lines.
- Keep console output to command intent, exit status, log paths, and a short failure excerpt.
- Preserve full logs under the repo-root `_tmp/dotnet/` directory so later investigation does not require re-running the command.

## Default Pattern

```sh
sh .codex/skills/dotnet-minimal-output/scripts/dotnet-quiet.sh \
  --label test-solution \
  -- dotnet test MySolution.sln --nologo --verbosity minimal
```

- Pass the complete command after `--`.
- Keep the wrapped CLI quiet by default with `--nologo` and `--verbosity quiet` or `--verbosity minimal` when the command supports it.
- Use a stable `--label` that describes intent such as `restore-api`, `build-worker`, or `test-solution`.
- Read [references/command-patterns.md](references/command-patterns.md) when choosing flags for common workflows.

## Command Rules

- Run directly only when the command is already tiny, such as `dotnet --version`.
- Use `--results-dir` only when the task needs logs somewhere other than `_tmp/dotnet`.
- Reuse the same label for retries so the log history stays easy to scan.
- If a failure needs more detail, retry with higher native verbosity while still using the wrapper and log files.

## Failure Handling

- Inspect `stderr.log` first, then `stdout.log`, then the shell-quoted `command.txt`.
- Quote only the smallest relevant excerpt back to the user.
- Report the run directory path so another agent can inspect the complete logs.
- Avoid dumping full test output or restore output into the thread unless the user explicitly asks for it.
