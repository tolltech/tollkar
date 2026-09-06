import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { Brand } from '../Brand'
import { approvePairingRequest, readPairingRequest, rejectPairingRequest } from './api'
import { useCurrentUser } from './currentUser'
import { formatUserCode, type PairingOutcome } from './pairing'

const outcomeMessages: Record<Exclude<PairingOutcome, 'pending'>, string> = {
  approved: 'Вход уже подтверждён. Вернитесь к экрану, который показывал код.',
  expired: 'Код устарел. Обновите QR-код на экране и отсканируйте его снова.',
  unknown: 'Код не найден. Обновите QR-код на экране и отсканируйте его снова.',
  forbidden: 'Гостевая сессия не может подтверждать вход на другом устройстве.',
  conflict: 'Этот код уже подтвердила другая учётная запись. Обновите QR-код на экране.'
}

export function PairDevicePage() {
  const { code = '' } = useParams()
  const user = useCurrentUser()
  const navigate = useNavigate()
  // A guest shares somebody else's session and is refused before the request is even looked up.
  const [result, setResult] = useState<{ code: string; outcome: PairingOutcome } | undefined>(
    user.isGuest ? { code, outcome: 'forbidden' } : undefined)
  const [notice, setNotice] = useState('')
  const [error, setError] = useState('')
  const [pending, setPending] = useState(false)

  useEffect(() => {
    if (user.isGuest) return
    const controller = new AbortController()
    readPairingRequest(code, controller.signal)
      .then(outcome => setResult({ code, outcome }))
      .catch(reason => {
        if (!controller.signal.aborted) setError(message(reason))
      })
    return () => controller.abort()
  }, [code, user.isGuest])

  const outcome = result?.code === code ? result.outcome : undefined

  async function confirm() {
    if (pending) return
    setPending(true)
    setError('')
    try {
      const confirmed = await approvePairingRequest(code)
      setResult({ code, outcome: confirmed })
      if (confirmed === 'approved') setNotice('Готово. Экран войдёт в вашу сессию.')
    } catch (reason) {
      setError(message(reason))
    } finally {
      setPending(false)
    }
  }

  async function decline() {
    if (pending) return
    setPending(true)
    setError('')
    try {
      await rejectPairingRequest(code)
      navigate('/queue', { replace: true })
    } catch (reason) {
      setError(message(reason))
      setPending(false)
    }
  }

  return (
    <main className="login-page">
      <section className="login-card" aria-labelledby="pair-title">
        <div className="brand login-brand"><Brand /></div>
        <p className="eyebrow">Веб-караоке</p>
        <h1 id="pair-title">Вход на другом экране</h1>
        {!outcome && !error && <p role="status">Проверяем код…</p>}
        {notice && <p className="app-notice" role="status">{notice}</p>}
        {outcome && outcome !== 'pending' && !notice &&
          <p className="app-notice" role="status">{outcomeMessages[outcome]}</p>}
        {outcome === 'pending' && <>
          <p>Экран запрашивает доступ к вашей очереди и плееру.</p>
          <p className="device-login-key">Код подтверждения: <strong>{formatUserCode(code)}</strong></p>
          <p>Разрешайте вход, только если этот код совпадает с кодом на экране перед вами.</p>
          <div className="pair-actions">
            <button className="primary-button" type="button" disabled={pending} onClick={() => void confirm()}>
              {pending ? 'Подтверждаем…' : 'Разрешить'}
            </button>
            <button className="secondary-button" type="button" disabled={pending} onClick={() => void decline()}>
              Отклонить
            </button>
          </div>
        </>}
        {error && <p className="auth-error" role="alert">{error}</p>}
        {outcome && outcome !== 'pending' &&
          <button className="secondary-button" type="button" onClick={() => navigate('/queue', { replace: true })}>
            К очереди
          </button>}
      </section>
    </main>
  )
}

function message(reason: unknown) {
  return reason instanceof Error ? reason.message : 'Не удалось выполнить запрос. Повторите попытку.'
}
