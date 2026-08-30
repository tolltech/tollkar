import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { submitAuth } from './api'

export function LoginPage() {
  const navigate = useNavigate()
  const [mode, setMode] = useState<'login' | 'register'>('login')
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
      await submitAuth(mode, { login, password })
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
        <h1 id="login-title">{mode === 'login' ? 'Вход' : 'Регистрация'}</h1>
        <form className="auth-form" onSubmit={submit} aria-busy={pending}>
          <label htmlFor="login">Логин</label>
          <input id="login" name="username" autoComplete="username" required maxLength={256} value={login} onChange={event => setLogin(event.target.value)} disabled={pending} />
          <label htmlFor="password">Пароль</label>
          <input id="password" name="password" type="password" autoComplete={mode === 'login' ? 'current-password' : 'new-password'} required maxLength={1024} value={password} onChange={event => setPassword(event.target.value)} disabled={pending} aria-describedby={mode === 'register' ? 'password-help' : undefined} />
          {mode === 'register' && <p id="password-help" className="page-description">Не менее 6 символов: строчная и заглавная латинские буквы, цифра и специальный символ.</p>}
          {error && <p className="auth-error" role="alert">{error}</p>}
          <button className="primary-button" disabled={pending} type="submit">{pending ? 'Отправляем…' : mode === 'login' ? 'Войти' : 'Зарегистрироваться'}</button>
          <button className="secondary-button" type="button" disabled={pending} onClick={() => { setMode(mode === 'login' ? 'register' : 'login'); setError(''); setPassword('') }}>
            {mode === 'login' ? 'Создать аккаунт' : 'Уже есть аккаунт? Войти'}
          </button>
        </form>
      </section>
    </main>
  )
}
