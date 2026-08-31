import { useEffect, useState } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { getCurrentUser, type User } from './api'
import { CurrentUserContext } from './currentUser'

export function RequireUser() {
  const { pathname } = useLocation()
  const [result, setResult] = useState<{ path: string; user?: User | null; error?: string }>()
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    getCurrentUser(controller.signal)
      .then(user => setResult({ path: pathname, user }))
      .catch(() => {
        if (!controller.signal.aborted) setResult({ path: pathname, error: 'Не удалось проверить сессию.' })
      })
    return () => controller.abort()
  }, [pathname, attempt])

  if (!result || result.path !== pathname) return <main className="app-content" role="status">Проверяем сессию…</main>
  if (result.error) return <main className="app-content"><p role="alert">{result.error}</p><button onClick={() => { setResult(undefined); setAttempt(value => value + 1) }}>Повторить</button></main>
  if (!result.user) return <Navigate to="/login" replace />
  return <CurrentUserContext.Provider value={result.user}><Outlet /></CurrentUserContext.Provider>
}
