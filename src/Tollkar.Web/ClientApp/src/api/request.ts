export async function mutate(path: string, method: 'POST' | 'DELETE', body?: unknown) {
  // Tokens are bound to the current identity, so obtain one for each mutation.
  const tokenResponse = await fetch('/api/auth/csrf', { credentials: 'same-origin' })
  if (!tokenResponse.ok) throw new Error('Не удалось подготовить запрос. Проверьте соединение и войдите снова.')
  const { token } = await tokenResponse.json() as { token: string }
  const response = await fetch(path, {
    method,
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { errors?: Record<string, string[]> } | null
    const message = response.status === 401 ? 'Сессия истекла. Войдите снова.'
      : response.status === 404 ? 'Песня больше недоступна. Обновите поиск.'
      : 'Не удалось выполнить запрос. Проверьте состояние очереди перед повторной попыткой.'
    throw new Error(problem?.errors ? Object.values(problem.errors).flat().join(' ') : message)
  }
}
