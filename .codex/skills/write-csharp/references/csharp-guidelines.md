# AggregatedMetrics C# Guidelines

## Code And Contracts

- Treat existing interfaces, DTOs, controllers, and client libraries as compatibility boundaries. Preserve their visibility, naming, and serialization shape unless the request changes the contract.
- Match the neighboring choice of `class`, `record`, inheritance, mutability, explicit types, and `var`. Add `sealed` or reduce visibility only when the type is demonstrably not extended or consumed externally.
- Do not add an `Async` suffix to an override or established interface member merely for style. For new standalone APIs, follow the nearest subsystem.
- Keep nullable reference types enabled and avoid unexplained null-forgiving operators.
- Use `default` for optional `CancellationToken` parameters when the surrounding public API does so; keep implementation signatures aligned with interfaces.
- Prefer async I/O and propagate cancellation where supported. Avoid `.Result`, `.Wait()`, and fire-and-forget tasks.

## Logging, DI, And Data

- Use `Vostok.Logging.Abstractions.ILog` in existing services and clients. For every error log, pass exactly one non-interpolated message-template string and supply property values as separate arguments so the template overload is selected; do not build the message with `+` or interpolation. Pass exceptions to the exception overload when stack information matters, and never log secrets or full sensitive payloads.
- Match existing constructor injection and service-registration patterns. Resolve services manually only in startup, test composition, or established infrastructure glue.
- Keep Newtonsoft.Json in clients and contracts that already depend on it. Use the serializer already selected by the affected boundary rather than mixing serializers incidentally.
- Keep existing explicit or extension-method mappings. Do not introduce Mapperly or another mapping dependency for an isolated edit.
- Preserve enum values and wire names. Treat DTO property renames and nullability changes as contract changes.

## Projects And Persistence

- Preserve the current solution layout and project naming; do not impose a new `src/` or `tests/` hierarchy.
- Keep `.csproj` indentation and package versions consistent with the file and root `.editorconfig`.
- For EF Core changes, identify the `DbContext`, migrations project, and startup project explicitly. Generate migrations with the pinned tool, inspect `Up`, `Down`, and SQL, and apply only to a local or disposable database unless authorized.
- Do not manually edit generated model snapshots or designer metadata; change the model and regenerate.
