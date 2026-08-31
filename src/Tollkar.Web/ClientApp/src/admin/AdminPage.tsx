import { useState, type FormEvent } from 'react'
import { createUser } from '../auth/api'

export function AdminPage() {
  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending) return
    setPending(true)
    setError('')
    setNotice('')
    try {
      await createUser({ login, password })
      setNotice(`Пользователь ${login} создан.`)
      setLogin('')
      setPassword('')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Ошибка соединения. Повторите попытку.')
    } finally {
      setPending(false)
    }
  }

  return (
    <section className="page admin-page" aria-labelledby="admin-title">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Управление доступом</p>
          <h1 id="admin-title">Администрирование</h1>
          <p className="page-description">Создавайте учётные записи для пользователей Tollkar.</p>
        </div>
      </div>
      <section className="admin-panel" aria-labelledby="create-user-title">
        <h2 id="create-user-title">Новый пользователь</h2>
        <form className="auth-form" onSubmit={submit} aria-busy={pending}>
          <label htmlFor="new-user-login">Логин</label>
          <input
            id="new-user-login"
            name="username"
            autoComplete="off"
            required
            maxLength={256}
            value={login}
            onChange={event => setLogin(event.target.value)}
            disabled={pending}
          />
          <label htmlFor="new-user-password">Пароль</label>
          <input
            id="new-user-password"
            name="new-password"
            type="password"
            autoComplete="new-password"
            required
            maxLength={1024}
            value={password}
            onChange={event => setPassword(event.target.value)}
            disabled={pending}
            aria-describedby="new-user-password-help"
          />
          <p id="new-user-password-help" className="page-description">
            Не менее 6 символов: строчная и заглавная латинские буквы, цифра и специальный символ.
          </p>
          {notice && <p className="admin-notice" role="status">{notice}</p>}
          {error && <p className="auth-error" role="alert">{error}</p>}
          <button className="primary-button" disabled={pending} type="submit">
            {pending ? 'Создаём…' : 'Создать пользователя'}
          </button>
        </form>
      </section>
    </section>
  )
}
