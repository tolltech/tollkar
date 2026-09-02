import { useEffect, useState } from 'react'

type Access = { imageUrl: string; expiresAt: string }

export function GuestAccess() {
  const [visible, setVisible] = useState(true)
  const [access, setAccess] = useState<Access>()
  const [error, setError] = useState('')

  useEffect(() => {
    if (!visible) return
    const controller = new AbortController()
    let timer: ReturnType<typeof setTimeout> | undefined

    async function load() {
      try {
        const response = await fetch('/api/guest/access', { credentials: 'same-origin', signal: controller.signal })
        if (!response.ok) throw new Error()
        const next = await response.json() as Access
        setAccess(next)
        setError('')
        const delay = Math.max(1000, new Date(next.expiresAt).getTime() - Date.now() + 1000)
        timer = setTimeout(load, delay)
      } catch {
        if (!controller.signal.aborted) {
          setAccess(undefined)
          setError('Не удалось подготовить гостевой QR-код. Повторяем попытку…')
          timer = setTimeout(load, 10_000)
        }
      }
    }

    void load()
    return () => {
      controller.abort()
      if (timer) clearTimeout(timer)
    }
  }, [visible])

  return <section className="guest-access" aria-labelledby="guest-access-title">
    <div className="guest-access-heading">
      <div>
        <h2 id="guest-access-title">Подключить гостей</h2>
        <p>Отсканируйте QR-код, чтобы открыть эту очередь и плеер без входа.</p>
      </div>
      <button className="secondary-button" onClick={() => setVisible(value => !value)}>
        {visible ? 'Скрыть QR-код' : 'Показать QR-код'}
      </button>
    </div>
    {visible && <div className="guest-access-code">
      {access && <img src={access.imageUrl} alt="QR-код для гостевого доступа к очереди" />}
      {!access && !error && <span role="status">Создаём QR-код…</span>}
      {error && <span role="alert">{error}</span>}
      <small>Код действует до конца текущей календарной даты на сервере.</small>
    </div>}
  </section>
}
