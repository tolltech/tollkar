# Layout, Overlays, And Adaptivity

- Use `ResponsiveLayout` or `useResponsiveLayout` for retail-ui mobile and desktop behavior instead of parallel `window.innerWidth` state.
- Preserve focus visibility, keyboard access, Escape handling, and focus return for menus, dialogs, popups, and side pages.
- Use public overlay props and supported ref contracts. Do not depend on internal DOM structure or revive `findDOMNode`-based patterns.
- For custom overlay children, forward the required ref or root-node contract used by the installed component version.
- Use `RenderEnvironmentProvider` only for separate roots, iframes, shadow DOM, or other isolated documents that need their own portal and style target.
- Prefer wrapper containers for page composition and retail-ui layout helpers for simple control spacing.
- In tests, mock `matchMedia` when needed and verify closed, open, keyboard, and focus paths relevant to the change.
