import { useEffect, useState, type FormEvent } from 'react'
import { createUser } from '../auth/api'
import { deleteAdminSong, loadAdminSongs, type AdminSong, type AdminSongCatalog } from './api'

export function AdminPage() {
  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [songSearch, setSongSearch] = useState('')
  const [catalog, setCatalog] = useState<AdminSongCatalog | null>(null)
  const [catalogLoading, setCatalogLoading] = useState(true)
  const [catalogError, setCatalogError] = useState('')
  const [deletingId, setDeletingId] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    const timer = setTimeout(async () => {
      try {
        setCatalogLoading(true)
        setCatalogError('')
        setCatalog(await loadAdminSongs(songSearch, controller.signal))
      } catch (reason) {
        if (!controller.signal.aborted) setCatalogError(reason instanceof Error ? reason.message : 'Не удалось загрузить каталог.')
      } finally {
        if (!controller.signal.aborted) setCatalogLoading(false)
      }
    }, 300)
    return () => { clearTimeout(timer); controller.abort() }
  }, [songSearch])

  async function removeSong(song: AdminSong) {
    if (!window.confirm(`Удалить файл «${song.title}»?`)) return
    setDeletingId(song.id)
    setCatalogError('')
    try {
      await deleteAdminSong(song.id)
      setCatalog(current => current && {
        ...current,
        items: current.items.filter(item => item.id !== song.id),
        totalCount: Math.max(0, current.totalCount - 1),
        matchedCount: Math.max(0, current.matchedCount - 1),
      })
    } catch (reason) {
      setCatalogError(reason instanceof Error ? reason.message : 'Не удалось удалить песню.')
    } finally {
      setDeletingId(null)
    }
  }

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
      <section className="admin-panel admin-catalog-panel" aria-labelledby="song-catalog-title">
        <h2 id="song-catalog-title">Каталог песен</h2>
        <label htmlFor="admin-song-search">Название или исполнитель</label>
        <input id="admin-song-search" type="search" value={songSearch} placeholder="Начните вводить…"
          onChange={event => setSongSearch(event.target.value)} />
        {catalog && <p className="page-description" aria-live="polite">
          Всего: {catalog.totalCount}. Найдено: {catalog.matchedCount}.
          {catalog.matchedCount > catalog.items.length && ' Показаны первые 500 строк.'}
        </p>}
        {catalogLoading && <p role="status">Загружаем каталог…</p>}
        {catalogError && <p className="auth-error" role="alert">{catalogError}</p>}
        {!catalogLoading && catalog && catalog.items.length === 0 && <p className="page-description">Песни не найдены.</p>}
        {catalog && catalog.items.length > 0 && <div className="admin-song-table-wrap">
          <table className="admin-song-table">
            <thead><tr><th>Исполнитель</th><th>Название</th><th>Папка</th><th>Запуски</th><th /></tr></thead>
            <tbody>{catalog.items.map(song => <tr key={song.id}>
              <td>{song.artist ?? '—'}</td><td>{song.title}</td><td>{song.folder ?? '—'}</td><td>{song.playCount}</td>
              <td><button className="secondary-button" disabled={deletingId !== null} onClick={() => removeSong(song)}>
                {deletingId === song.id ? 'Удаляем…' : 'Удалить'}
              </button></td>
            </tr>)}</tbody>
          </table>
        </div>}
      </section>
    </section>
  )
}
