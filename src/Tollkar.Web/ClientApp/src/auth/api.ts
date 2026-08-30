import { mutate } from '../api/request'

export type User = { id: string; login: string }

export async function getCurrentUser(signal?: AbortSignal): Promise<User | null> {
  const response = await fetch('/api/auth/me', { credentials: 'same-origin', signal })
  if (response.status === 401) return null
  if (!response.ok) throw new Error('Не удалось проверить сессию. Повторите попытку.')
  return response.json()
}

export async function submitAuth(action: 'login' | 'register' | 'logout', credentials?: { login: string; password: string }) {
  await mutate(`/api/auth/${action}`, 'POST', credentials)
}
