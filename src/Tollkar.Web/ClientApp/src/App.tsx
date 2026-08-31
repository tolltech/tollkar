import { useState } from 'react'
import { Navigate, NavLink, Outlet, Route, Routes, useNavigate } from 'react-router-dom'
import { AdminPage } from './admin/AdminPage'
import { LoginPage } from './auth/LoginPage'
import { RequireAdmin } from './auth/RequireAdmin'
import { RequireUser } from './auth/RequireUser'
import { submitAuth } from './auth/api'
import { canAccessAdmin } from './auth/authorization'
import { useCurrentUser } from './auth/currentUser'
import './App.css'
import { useQueue } from './queue/useQueue'
import { PlayerPage } from './player/PlayerPage'
import { QueuePage } from './queue/QueuePage'

function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireUser />}>
        <Route element={<AppLayout />}>
          <Route path="/queue" element={<QueuePage />} />
          <Route path="/player" element={<PlayerPage />} />
          <Route element={<RequireAdmin />}>
            <Route path="/admin" element={<AdminPage />} />
          </Route>
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/login" replace />} />
    </Routes>
  )
}

function AppLayout() {
  const user = useCurrentUser()
  const queue = useQueue(user.id)
  const navigate = useNavigate()
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')

  async function logout() {
    setPending(true)
    setError('')
    try {
      await submitAuth('logout')
      navigate('/login', { replace: true })
    } catch {
      setError('Не удалось выйти. Повторите попытку.')
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="app-shell">
      <header className="app-header">
        <NavLink className="brand" to="/queue" aria-label="Tollkar — к очереди">
          <span className="brand-mark" aria-hidden="true">T</span>
          <span>Tollkar</span>
        </NavLink>
        <nav className="primary-navigation" aria-label="Основная навигация">
          <NavLink to="/queue">Очередь</NavLink>
          <NavLink to="/player">Плеер</NavLink>
          {canAccessAdmin(user) && <NavLink to="/admin">Администрирование</NavLink>}
        </nav>
        <div className="user-menu"><span>{user.login}</span><button className="secondary-button" disabled={pending} onClick={logout}>{pending ? 'Выходим…' : 'Выйти'}</button></div>
      </header>
      <main className="app-content">
        {error && <p role="alert">{error}</p>}
        <Outlet context={queue} />
      </main>
    </div>
  )
}

export default App
