import { mutate } from '../api/request'

export type User = { id: string; login: string; isAdmin: boolean; isGuest: boolean }

export async function getCurrentUser(signal?: AbortSignal): Promise<User | null> {
  const response = await fetch('/api/auth/me', { credentials: 'same-origin', signal })
  if (response.status === 401) return null
  if (!response.ok) throw new Error('Не удалось проверить сессию. Повторите попытку.')
  return response.json()
}

export async function submitAuth(action: 'login' | 'logout', credentials?: { login: string; password: string }) {
  await mutate(`/api/auth/${action}`, 'POST', credentials)
}

export async function createUser(credentials: { login: string; password: string }) {
  await mutate('/api/auth/register', 'POST', credentials,
    'Не удалось создать пользователя. Проверьте данные и повторите попытку.')
}
