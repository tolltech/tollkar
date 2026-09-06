import { mutate, sendMutation } from '../api/request'
import type { PairingOutcome, PairingRequest } from './pairing'

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

export async function createPairingRequest(signal?: AbortSignal): Promise<PairingRequest> {
  const response = await fetch('/api/pairing/requests', {
    method: 'POST',
    credentials: 'same-origin',
    ...(signal ? { signal } : {})
  })
  if (!response.ok) throw new Error('Не удалось подготовить QR-код для входа.')
  return await response.json() as PairingRequest
}

/** Anonymous by design: only the device code, kept in this tab, turns a confirmation into a session. */
export async function claimPairingSession(request: PairingRequest, signal?: AbortSignal): Promise<PairingOutcome> {
  const response = await fetch('/api/pairing/session', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ userCode: request.userCode, deviceCode: request.deviceCode }),
    ...(signal ? { signal } : {})
  })
  return await readPairingOutcome(response)
}

export async function readPairingRequest(userCode: string, signal?: AbortSignal): Promise<PairingOutcome> {
  const response = await fetch('/api/pairing/requests/' + encodeURIComponent(userCode), {
    credentials: 'same-origin',
    ...(signal ? { signal } : {})
  })
  return await readPairingOutcome(response)
}

export async function approvePairingRequest(userCode: string): Promise<PairingOutcome> {
  const response = await sendMutation('/api/pairing/requests/' + encodeURIComponent(userCode) + '/approve', 'POST')
  return response.status === 204
    ? 'approved'
    : await readPairingOutcome(response, 'Не удалось подтвердить вход. Повторите попытку.')
}

/** A code that is gone is as declined as one that never existed, so both count as success. */
export async function rejectPairingRequest(userCode: string): Promise<void> {
  const response = await sendMutation('/api/pairing/requests/' + encodeURIComponent(userCode), 'DELETE')
  if (response.ok || response.status === 404 || response.status === 410) return
  throw new Error(response.status === 401
    ? 'Сессия истекла. Войдите снова.'
    : 'Не удалось отклонить запрос на вход. Повторите попытку.')
}

async function readPairingOutcome(response: Response,
  failureMessage = 'Не удалось проверить запрос на вход. Повторите попытку.'): Promise<PairingOutcome> {
  if (response.status === 403) return 'forbidden'
  if (response.status === 404) return 'unknown'
  if (response.status === 409) return 'conflict'
  if (response.status === 410) return 'expired'
  if (response.status === 401) throw new Error('Сессия истекла. Войдите снова.')
  if (!response.ok) throw new Error(failureMessage)
  const body = await response.json() as { status?: string }
  return body.status === 'approved' ? 'approved' : 'pending'
}
