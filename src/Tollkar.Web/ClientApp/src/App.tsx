import { Navigate, NavLink, Outlet, Route, Routes } from 'react-router-dom'
import './App.css'

function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<AppLayout />}>
        <Route path="/queue" element={<QueuePage />} />
        <Route path="/player" element={<PlayerPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/login" replace />} />
    </Routes>
  )
}

function AppLayout() {
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
      </header>
      <main className="app-content">
        <Outlet />
      </main>
    </div>
  )
}

function LoginPage() {
  return (
    <main className="login-page">
      <section className="login-card" aria-labelledby="login-title">
        <div className="brand login-brand">
          <span className="brand-mark" aria-hidden="true">T</span>
          <span>Tollkar</span>
        </div>
        <p className="eyebrow">Веб-караоке</p>
        <h1 id="login-title">Ваша очередь. Ваш экран.</h1>
        <p className="page-description">
          Авторизация и персональные очереди появятся на следующем этапе.
        </p>
        <NavLink className="primary-button" to="/queue">Открыть прототип</NavLink>
      </section>
    </main>
  )
}

function QueuePage() {
  return (
    <section className="page queue-page" aria-labelledby="queue-title">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Пульт управления</p>
          <h1 id="queue-title">Очередь караоке</h1>
          <p className="page-description">Здесь появятся поиск песен, сортировка и управление очередью.</p>
        </div>
        <NavLink className="secondary-button" to="/player">Открыть плеер</NavLink>
      </div>
      <div className="empty-state">
        <span className="empty-state-icon" aria-hidden="true">♪</span>
        <h2>Очередь пока пуста</h2>
        <p>На следующем этапе подключим библиотеку и персональные очереди.</p>
      </div>
    </section>
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
    </section>
  )
}

export default App
