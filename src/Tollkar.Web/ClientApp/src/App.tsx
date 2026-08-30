import { useState } from 'react'
import { Navigate, NavLink, Outlet, Route, Routes, useNavigate, useOutletContext } from 'react-router-dom'
import { LoginPage } from './auth/LoginPage'
import { RequireUser } from './auth/RequireUser'
import { submitAuth, type User } from './auth/api'
import './App.css'
import { useQueue } from './queue/useQueue'
import { QueueState } from './queue/QueueState'
import { QueuePage } from './queue/QueuePage'

function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireUser />}>
        <Route element={<AppLayout />}>
          <Route path="/queue" element={<QueuePage />} />
          <Route path="/player" element={<PlayerPage />} />
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/login" replace />} />
    </Routes>
  )
}

function AppLayout() {
  const user = useOutletContext<User>()
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

function PlayerPage() {
  return (
    <section className="page player-page" aria-labelledby="player-title">
      <div className="player-stage">
        <div className="player-placeholder" aria-hidden="true">▶</div>
        <div className="player-copy">
          <p className="eyebrow">Экран воспроизведения</p>
          <h1 id="player-title">Готов к подключению</h1>
          <p>Видео и синхронизированные команды появятся на следующих этапах.</p>
        </div>
      </div>
      <QueueState />
    </section>
  )
}

export default App
