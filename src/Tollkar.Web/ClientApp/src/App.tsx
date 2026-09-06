import { Navigate, NavLink, Outlet, Route, Routes, useLocation } from 'react-router-dom'
import { AdminPage } from './admin/AdminPage'
import { Brand } from './Brand'
import { LoginPage } from './auth/LoginPage'
import { PairDevicePage } from './auth/PairDevicePage'
import { RequireAdmin } from './auth/RequireAdmin'
import { LogoutButton } from './auth/LogoutButton'
import { RequireUser } from './auth/RequireUser'
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
        <Route path="/pair/:code" element={<PairDevicePage />} />
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
  const { pathname } = useLocation()

  // A display exists to show the player: whatever page its television restores, it lands there.
  if (user.isDisplay && pathname !== '/player') return <Navigate to="/player" replace />

  return (
    <div className={`app-shell${user.isDisplay ? ' is-display' : ''}`}>
      <header className="app-header">
        <NavLink className="brand" to="/queue" aria-label="Tollkar — к очереди">
          <Brand />
        </NavLink>
        <nav className="primary-navigation" aria-label="Основная навигация">
          <NavLink to="/queue">Очередь</NavLink>
          <NavLink to="/player">Плеер</NavLink>
          {canAccessAdmin(user) && <NavLink to="/admin">Администрирование</NavLink>}
        </nav>
        <div className="user-menu"><span>{user.login}</span><LogoutButton /></div>
      </header>
      <main className="app-content">
        <Outlet context={queue} />
      </main>
    </div>
  )
}

export default App
