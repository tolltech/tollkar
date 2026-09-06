import { useEffect, useState, type FormEvent } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { Brand } from '../Brand'
import { getCurrentUser, submitAuth } from './api'
import { DeviceLogin } from './DeviceLogin'

export function LoginPage() {
  const navigate = useNavigate()
  const destination = (useLocation().state as { from?: string } | null)?.from ?? '/queue'
  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')

  // A display restarted by its television lands here with a live session: send it back to the player.
  useEffect(() => {
    const controller = new AbortController()
    getCurrentUser(controller.signal)
      .then(user => { if (user?.isDisplay) navigate('/player', { replace: true }) })
      .catch(() => {})
    return () => controller.abort()
  }, [navigate])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending) return
    setPending(true)
    setError('')
    try {
      await submitAuth('login', { login, password })
      setPassword('')
      navigate(destination, { replace: true })
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Ошибка соединения. Повторите попытку.')
    } finally {
      setPending(false)
    }
  }

  return (
    <main className="login-page">
      <section className="login-card" aria-labelledby="login-title">
        <div className="brand login-brand"><Brand /></div>
        <p className="eyebrow">Веб-караоке</p>
        <h1 id="login-title">Вход</h1>
        <form className="auth-form" onSubmit={submit} aria-busy={pending}>
          <label htmlFor="login">Логин</label>
          <input id="login" name="username" autoComplete="username" required maxLength={256} value={login} onChange={event => setLogin(event.target.value)} disabled={pending} />
          <label htmlFor="password">Пароль</label>
          <input id="password" name="password" type="password" autoComplete="current-password" required maxLength={1024} value={password} onChange={event => setPassword(event.target.value)} disabled={pending} />
          {error && <p className="auth-error" role="alert">{error}</p>}
          <button className="primary-button" disabled={pending} type="submit">{pending ? 'Отправляем…' : 'Войти'}</button>
        </form>
        <DeviceLogin />
      </section>
    </main>
  )
}
