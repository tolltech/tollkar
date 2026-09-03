import { useEffect, useState } from 'react'

type Access = { imageUrl: string; expiresAt: string; url: string }

async function requestAccess(signal?: AbortSignal) {
  const response = await fetch('/api/guest/access', {
    credentials: 'same-origin',
    ...(signal ? { signal } : {})
  })
  if (!response.ok) throw new Error()
  return await response.json() as Access
}

export function GuestAccess() {
  const [visible, setVisible] = useState(false)
  const [access, setAccess] = useState<Access>()
  const [error, setError] = useState('')
  const [shareNotice, setShareNotice] = useState('')
  const [sharing, setSharing] = useState(false)

  useEffect(() => {
    if (!visible) return
    const controller = new AbortController()
    let timer: ReturnType<typeof setTimeout> | undefined

    async function load() {
      try {
        const next = await requestAccess(controller.signal)
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

  async function shareSession() {
    if (sharing) return
    setSharing(true)
    setError('')
    setShareNotice('')
    try {
      const isValid = access && new Date(access.expiresAt).getTime() > Date.now()
      const current = isValid ? access : await requestAccess()
      setAccess(current)
      if (navigator.share) {
        try {
          await navigator.share({
            title: 'Сессия Tollkar',
            text: 'Подключитесь к сессии караоке',
            url: current.url
          })
          setShareNotice('Сессия готова к подключению.')
          return
        } catch (reason) {
          if (reason instanceof DOMException && reason.name === 'AbortError') return
        }
      }
      if (navigator.clipboard) {
        await navigator.clipboard.writeText(current.url)
        setShareNotice('Ссылка на сессию скопирована.')
      } else {
        throw new Error()
      }
    } catch (reason) {
      if (reason instanceof DOMException && reason.name === 'AbortError') return
      setError('Не удалось поделиться сессией. Повторите попытку.')
    } finally {
      setSharing(false)
    }
  }

  return <section className="guest-access" aria-labelledby="guest-access-title">
    <div className="guest-access-heading">
      <div>
        <h2 id="guest-access-title">Подключить гостей</h2>
        <p>Отсканируйте QR-код, чтобы открыть эту очередь и плеер без входа.</p>
      </div>
      <div className="guest-access-actions">
        <button type="button" className="secondary-button" aria-controls="guest-access-code" aria-expanded={visible}
          onClick={() => setVisible(value => !value)}>
          {visible ? 'Скрыть QR-код' : 'Показать QR-код'}
        </button>
        <button type="button" className="primary-button" disabled={sharing} onClick={() => void shareSession()}>
          {sharing ? 'Подготавливаем…' : 'Поделиться сессией'}
        </button>
      </div>
    </div>
    {shareNotice && <p className="guest-access-feedback" role="status">{shareNotice}</p>}
    {error && <p className="guest-access-feedback is-error" role="alert">{error}</p>}
    {visible && <div id="guest-access-code" className="guest-access-code">
      {access && <img src={access.imageUrl} alt="QR-код для гостевого доступа к очереди" />}
      {!access && !error && <span role="status">Создаём QR-код…</span>}
      <small>Код действует до конца текущей календарной даты на сервере.</small>
    </div>}
  </section>
}
