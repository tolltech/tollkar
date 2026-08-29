# AggregatedMetrics Testing Guidelines

## Stack And Shape

- Use dependencies already referenced by the target test project. `AggregatedMetrics.Tests` uses NUnit 4, FluentAssertions, and NSubstitute; browser tests use NUnit and Playwright without those extra packages.
- Preserve `[TestFixture]`, fixture lifecycle, setup, teardown, and parallelization attributes used by the nearest tests.
- Name every new or changed NUnit test method `Test...` in CamelCase. Give the remainder of the name a clear behavior-oriented meaning without renaming unrelated existing coverage.
- Keep one scenario per test and use helpers or builders when setup becomes repetitive.

## Reliability

- Make tests deterministic and independent of wall-clock timing where practical.
- Use temporary directories for filesystem coverage and avoid live network calls unless the test suite explicitly provides that integration.
- Reset shared state and substitutes through the fixture lifecycle already used by the test area.
- Prefer black-box tests that exercise public behavior and assert externally observable results over tests coupled to internal collaboration.
- Use mocks and substitutes to supply input data or dependency responses. Do not assert call counts, call order, or similar implementation details unless the interaction itself is part of the system contract.
- Keep permanent tests only for system-core behavior, genuinely complex logic, or non-trivial joins, mappings, and conversions. Do not retain tests for simple database queries, simple network requests, simple metric calculators, or simple issue detectors.
- For a bug fix, demonstrate the failure with a focused regression test before changing production behavior. A test written only for agent self-checking may be removed before delivery when the behavior does not meet the permanent-test criteria.

## Verification

- Run the narrow test project or filtered fixture first using the repository's quiet dotnet wrapper.
- Widen to the solution gate when the change affects shared contracts, DI, persistence, or serialization.
