# Package And Component Selection

## Imports

- Import `Button`, `DropdownMenu`, `Input`, `Link`, and `Select` from `shared/components`. The repository's Biome configuration rejects direct imports of these names from `@skbkontur/react-ui`.
- Check `shared/components` for another suitable wrapper before importing a public retail-ui component directly.
- Import controls without a repository wrapper from `@skbkontur/react-ui` and validations from `@skbkontur/react-ui-validations`.
- Avoid deep internal paths. Preserve an existing type-only deep import until the installed public API provides the same type and a deliberate migration is in scope.
- Use installed adjacent Kontur packages for their domain: icons, colors, typography, logos, side menu, tables, empty states, and standard error pages.

## Selection

- Use `Button` for in-page actions and `Link` for navigation. Set button `type` explicitly in forms.
- Use `Input` or `Textarea` for text, `Select` or `RadioGroup` for small fixed sets, and searchable controls only when search is part of the interaction.
- Use `DatePicker` or `DateRangePicker` for dates, `Modal` or `SidePage` for large overlays, and `Hint` or `Tooltip` for contextual help.
- Keep controls controlled with `value` or `checked` and `onValueChange`.
- Prefer the simplest component that satisfies accessibility, mobile behavior, and the requested UX.

## Migration Watchlist

- Prefer current retail-ui 6.x props and validations 3.x APIs installed by the repository.
- Replace legacy `Button use="link"` with the repository `Link` wrapper rendered as a button when appropriate.
- Replace removed `Input mask`, `Toast.push()`, `hideScrollBar`, and global-window helpers only when encountered in the changed area.
- Do not introduce temporary production feature flags for migrations.
