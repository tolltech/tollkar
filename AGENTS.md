# Agent Guidelines

Guidance for AI agents contributing to this repository.

## General guidelines

Priority order for instructions (highest to lowest):
1. System instructions.
2. User instructions.
3. `AGENTS.project.md` if it exists.
4. Repo-specific instructions.

## Workflow

- Before returning control, run `./handoff.sh` if any changes in code were made.
- If `./handoff.sh` fails or cannot be run, say why and run the closest available checks (build, lint, test); summarize failures with actionable detail.
- If `./handoff.sh` exists, keep it updated to match the current repo state, validation steps, and required tooling whenever your changes make it stale.
- For non-trivial changes (multi-file, user-facing behavior), request a sub-agent review.
- When requesting a review, you must always use `default` sub-agent type. Reuse same sub-agent for repeated changes instead of creating a new one each time. Create new review sub-agent when working on a new task.
- Do not leave TODO/FIXME markers in production code.
- Do not introduce temporary feature flags in production code unless the user explicitly asks for them.
- If implementation requires copy-paste, refactor duplicated logic before returning control.
- Proactively look at existing code when implementing a new feature to follow patterns and naming conventions.

## Code

- Keep functions, classes, and modules single-purpose and cohesive; split responsibilities instead of growing "do-everything" modules.
- Extract helpers instead of adding nesting.
- Prefer explicit, domain-aligned names; avoid boolean flag parameters.
- Use domain terms already established when naming domain models and service contracts.
- Do not introduce new domain boundaries unless requested.
- Remove avoidable duplication in files you touch.
- Use scaffolding tools generated code instead of writing it manually.
- Comments must explain rationale, invariants, or edge cases not inferable from code.
- Do not add comments that restate signatures, parameter names, or direct code flow.
- Always remove unused usings
- Follow .editorconfig

## Database Migrations

- Follow the repository's existing migration conventions and use its pinned migration tooling.
- Before generating a migration, identify the intended schema or data model, migration target, and startup project or equivalent; select them explicitly when the tooling finds multiple candidates.
- Generate migration artifacts with the framework tooling (for example, `dotnet ef migrations add`); do not fabricate migration identifiers, metadata, designer files, or schema snapshots.
- Review generated apply and rollback operations for destructive or unintended changes. Edit the operations when the generated migration does not safely represent the intended change, such as a data migration or a rename that preserves existing data.
- Do not manually edit tool-owned metadata, designer files, or schema snapshots. Change the model and regenerate the migration unless the repository's tooling documents a different workflow.
- Build the affected projects and validate the migration with the repository's tests, schema checks, and migration script or dry-run workflow. Generate an idempotent script when the database provider and deployment process support it.
- Apply migrations only to a local or disposable database unless the user explicitly authorizes the target database.

## Testing

- When fixing a bug, write a failing test first; then fix the bug and ensure the test passes.
- Do not disable/suppress warnings just to make checks pass; fix warnings in changed files (or explain why not).
- Keep tests deterministic (seed randomness; avoid wall-clock dependencies).
- Use temp directories for filesystem tests; avoid network unless explicitly designed
- Do not write tests for metric calculators and issue detectors. The only exception is when they contain genuinely complex logic or builders

## Communication

- Use concise, clear language.
- Use Russian by default unless the user explicitly asks for another language.
- Document assumptions and decisions.
- Keep context focused on the active task; use sub-agents for broad code exploration and reuse sub-agents for follow-ups to avoid context reloads.
- If additional context is required, write details to a temporary report file under `_tmp/` (git-ignored); share only the path and a brief summary.
- If a task uses multiple independent subagents, include a cross-review pass.
- In that pass, one subagent reviews another subagent's output.
- Never interrupt subagents performing reviews; let them complete and start a new review if direction changes.
- The review must check for conflicts, regressions, and missing coverage before final integration.
- The review must also check for guideline drift (deviations from `AGENTS.md`, `docs/`, and established repo conventions) and call out needed corrections.

## Repo Hygiene

- Avoid reformatting or touching unrelated files.
- Do not delete or revert user changes without asking.
- When repo structure, tooling, or conventions change, update the canonical agent guidance and documentation.
- Never introduce secrets, tokens, or credentials into the repo.
- If secrets appear in logs or output, mention them and avoid repeating them in full.
- Redact secrets in examples, screenshots, and logs (for example, replace with `REDACTED`).

## Shell Scripts

- Shell scripts must be POSIX `sh`-compatible (no bashisms).
- If you find a script that is not POSIX-compatible, update it to be POSIX-compatible.
- Use clear error messages and predictable exits.
- Prefer deterministic downloads and verification.
- Shell scripts must include brief comments explaining intent and non-obvious steps.

## Sensitive files

Never read, inspect, search, print, summarize, modify, or otherwise access:

- /Tools/**/*
- Content/**/*