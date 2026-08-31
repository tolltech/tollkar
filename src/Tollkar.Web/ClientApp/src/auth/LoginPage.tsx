import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { submitAuth } from './api'

export function LoginPage() {
  const navigate = useNavigate()
  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending) return
    setPending(true)
    setError('')
    try {
      await submitAuth('login', { login, password })
      setPassword('')
      navigate('/queue', { replace: true })
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Ошибка соединения. Повторите попытку.')
    } finally {
      setPending(false)
    }
  }

  return (
    <main className="login-page">
      <section className="login-card" aria-labelledby="login-title">
        <div className="brand login-brand"><span className="brand-mark" aria-hidden="true">T</span><span>Tollkar</span></div>
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
      </section>
    </main>
  )
}
