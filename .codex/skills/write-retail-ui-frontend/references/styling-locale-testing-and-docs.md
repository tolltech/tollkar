# Styling, Locale, Testing, And Docs

## Styling And Locale

- Prefer component props, composition, wrapper layout, and theme overrides before CSS overrides.
- Use `className` or `style` for outer placement; never target internal retail-ui class names.
- Keep theme overrides narrow and based on design intent. Preserve the application's existing theme and locale providers.
- Use locale APIs for control text and aria labels rather than patching rendered strings.
- Use the component `size` prop locally and `SizeControlContext.Provider` for subtree defaults.

## Accessibility

- Use labels for textbox controls, accessible names for button-backed selectors, and `aria-label` for icon-only actions.
- Preserve semantic headings, landmarks, native buttons and links, logical DOM order, zoom, and meaningful image alternatives.
- Keep action and navigation semantics correct instead of simulating them with ARIA roles.

## Tests And Stories

- Inspect the affected area's tests first. Vitest and React Testing Library packages are installed, but a DOM environment, configuration, and test script are not guaranteed to exist.
- When component-test infrastructure is in scope, configure only the minimum required by the task and prefer semantic queries.
- Reuse current test helpers and `data-tid` conventions. Do not add `user-event`, axe, Selenium, or screenshot tooling solely because an example recommends it.
- Mock browser APIs such as `matchMedia` only for tests that need them; keep tests deterministic.
- In Storybook, document behavior and relevant states: default, disabled or loading, validation, mobile, theme, or locale.
- Keep examples minimal, controlled, and aligned with repository wrapper imports.
