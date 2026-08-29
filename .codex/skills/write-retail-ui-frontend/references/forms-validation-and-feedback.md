# Forms, Validation, And Feedback

## Validation

- Use `ValidationContainer` for a submit boundary and `ValidationWrapper` around the smallest meaningful field unit.
- Use a direct `validationInfo` expression for small local forms. Use `createValidator` for object models, arrays, dependencies, or reusable rules.
- Derive validation from current form state. Do not cache a validation tree in unrelated state.
- Prefer `submit` validation for required fields, `lostfocus` for post-edit checks, and `immediate` only for feedback that must always be visible.
- Await `validate()` when saving depends on its boolean result. Use `submit()` only to reveal errors.
- Keep required labels explicit and prefer persistent text for accessibility-sensitive errors.

## Controls And Wrappers

- Keep controls controlled and preserve `ref`, `error`, `warning`, `onBlur`, `onValueChange`, and accessible-name props through custom wrappers.
- Strip retail-ui-only callbacks before spreading props onto native DOM nodes.
- Give text inputs a label or `aria-label`; give button-backed selectors an accessible name and a mobile header where supported.
- Keep one validation container per submit boundary and stable keys for dynamic rows.
- Treat hidden required data separately because a container validates rendered wrappers.

## Tests

- Cover invalid reveal, focus behavior when relevant, and recovery after correction.
- Query by role, label, accessible name, or text before using `data-tid`.
- Use the event utilities already installed and established in the test suite; do not require `@testing-library/user-event` unless it is deliberately added to the project.
- Test representative validation model states directly when rules are complex or shared.
