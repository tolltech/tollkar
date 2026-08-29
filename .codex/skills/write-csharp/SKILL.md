---
name: write-csharp
description: Write, refactor, review, or test C#/.NET code in the AggregatedMetrics repository. Use for changes to .cs, .csproj, .sln, Entity Framework Core models or migrations, dependency injection, Vostok logging, and NUnit tests while preserving the repository's established public APIs and legacy-compatible conventions.
---

# Write C#

## Start Here

- Inspect the root `.editorconfig`, `AGENTS.md`, affected projects, and neighboring code before editing.
- Follow repository configuration and established local patterns before generic .NET preferences.
- Read [references/csharp-guidelines.md](references/csharp-guidelines.md) for application, API, persistence, logging, DI, or project changes.
- Read [references/testing-guidelines.md](references/testing-guidelines.md) before adding or changing tests.
- Keep changes narrow. Do not modernize unrelated legacy code while implementing a feature or fix.

## Preserve Repository Conventions

- Target the framework and package versions already pinned by the affected project.
- Keep public accessibility, inheritance, async method names, serialization, and mapping style consistent with the surrounding contract. Do not impose `internal`, `sealed`, an `Async` suffix, Mapperly, or a serializer migration across existing boundaries.
- Keep `using` directives outside namespaces as required by `.editorconfig`; remove unused usings.
- Preserve nullable annotations and implicit-usings settings. Fix warnings in changed code instead of suppressing them.
- Use asynchronous APIs without `.Result` or `.Wait()`. Thread `CancellationToken` through changed public async flows when the existing contract supports it.
- Prefer cohesive classes, short methods, explicit domain names, and extracted helpers over added nesting or boolean flag parameters.

## Work With Existing Infrastructure

- Use Vostok `ILog` where the affected subsystem already uses it. Log errors with exactly one message-template string and pass values as separate arguments; do not concatenate or interpolate the message.
- Follow existing constructor injection and registration patterns; avoid service location outside infrastructure composition.
- Preserve Newtonsoft.Json or manual mapping where already part of a contract. Introduce a different serializer or mapper only when the task requires a deliberate migration.
- Follow the database migration workflow in the root `AGENTS.md`; generate EF artifacts with the pinned tooling and never fabricate snapshots or designer metadata.

## Test And Verify

- For bug fixes, add a failing regression test first unless the root guidance excludes that detector or calculator category; a self-check test may be removed before delivery when the behavior does not warrant permanent coverage.
- Use the target test project's existing stack. `AggregatedMetrics.Tests` uses NUnit, FluentAssertions, and NSubstitute; browser tests use NUnit and Playwright without requiring those assertion or mocking packages.
- Name new or changed NUnit test methods `Test...` in CamelCase; do not rename unrelated existing tests solely to enforce the convention.
- Prefer black-box tests of observable behavior. Use mocks to supply dependency data, not to assert call counts, ordering, or other implementation details unless those interactions are the contract.
- Keep permanent tests for system-core behavior, genuinely complex logic, or non-trivial joins, mappings, and conversions. Do not retain tests for simple database queries or simple network requests.
- Run focused tests first, then the repository handoff gate or the closest available checks.

## Deliver

- Summarize the repository conventions that shaped the change and any compatibility constraint that prevented a generic modernization.
