export type User = { id: string; login: string }

export async function getCurrentUser(signal?: AbortSignal): Promise<User | null> {
  const response = await fetch('/api/auth/me', { credentials: 'same-origin', signal })
  if (response.status === 401) return null
  if (!response.ok) throw new Error('Не удалось проверить сессию. Повторите попытку.')
  return response.json()
}

export async function submitAuth(action: 'login' | 'register' | 'logout', credentials?: { login: string; password: string }) {
  // Refresh after every identity change: antiforgery tokens are bound to the current user.
  const tokenResponse = await fetch('/api/auth/csrf', { credentials: 'same-origin' })
  if (!tokenResponse.ok) throw new Error('Не удалось подготовить запрос. Повторите попытку.')
  const { token } = await tokenResponse.json() as { token: string }
  const response = await fetch(`/api/auth/${action}`, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
    body: credentials ? JSON.stringify(credentials) : undefined,
  })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { errors?: Record<string, string[]> } | null
    throw new Error(problem?.errors ? Object.values(problem.errors).flat().join(' ') : 'Не удалось выполнить запрос. Повторите попытку.')
  }
}
