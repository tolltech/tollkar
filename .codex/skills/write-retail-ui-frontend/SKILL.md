---
name: write-retail-ui-frontend
description: Build, refactor, review, document, or test the AggregatedMetrics React frontend when work uses @skbkontur/react-ui, @skbkontur/react-ui-validations, shared UI wrappers, forms, overlays, theming, locale, responsive behavior, Storybook, or Vitest.
---

# Write Retail UI Frontend

## Start Here

- Inspect `AggregatedMetrics.Front/package.json`, `biome.json`, its nested `.editorconfig`, and neighboring components before editing.
- Work with the installed React 19, Vite, React Router, retail-ui 6.x, and validations 3.x APIs. Do not introduce Next.js patterns.
- Prefer the repository's shared components and hooks over direct library APIs when wrappers exist.
- Keep changes compatible with Biome, TypeScript strict mode, the 120-character line limit, and single-quoted JavaScript strings.

## Reference Map

- Read [references/package-and-component-selection.md](references/package-and-component-selection.md) when choosing components or imports.
- Read [references/forms-validation-and-feedback.md](references/forms-validation-and-feedback.md) for forms and validation.
- Read [references/layout-overlays-and-adaptivity.md](references/layout-overlays-and-adaptivity.md) for overlays, responsive behavior, and focus.
- Read [references/styling-locale-testing-and-docs.md](references/styling-locale-testing-and-docs.md) for styling, locale, tests, or Storybook.

## Repository Defaults

- Import `Button`, `DropdownMenu`, `Input`, `Link`, and `Select` from `shared/components`; Biome rejects direct imports of those names from `@skbkontur/react-ui`.
- Import other retail-ui controls from the public package entrypoint unless a local wrapper already owns the behavior.
- Keep value controls controlled and use `onValueChange` when exposed by the component.
- Use native action and navigation semantics, accessible names, visible focus, and correct keyboard behavior.
- Use `ResponsiveLayout` or `useResponsiveLayout` instead of custom viewport listeners when retail-ui behavior depends on layout mode.
- Use validations package primitives rather than ad hoc error state when editing an established validated form.
- Style through public props, composition, wrapper layout, and theme APIs; do not depend on internal DOM or class names.

## Test And Deliver

- Inspect the affected area's current test pattern before adding coverage. Vitest and React Testing Library packages are available, but component-test environment and scripts may need explicit task-scoped setup.
- Do not add `user-event`, an accessibility runner, or a visual-test stack unless the task requires it.
- Prefer semantic queries; use existing `data-tid` hooks only when a stable semantic query is unavailable.
- Run frontend lint and build checks, then the repository handoff gate when available.
- Summarize wrapper choices, version constraints, deprecated APIs, and verification performed.
