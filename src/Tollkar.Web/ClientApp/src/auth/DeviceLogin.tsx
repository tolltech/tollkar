import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { claimPairingSession, createPairingRequest } from './api'
import { formatUserCode, nextPairingStep, type PairingRequest } from './pairing'

export function DeviceLogin() {
  const navigate = useNavigate()
  const [visible, setVisible] = useState(false)
  const [request, setRequest] = useState<PairingRequest>()
  const [error, setError] = useState('')

  useEffect(() => {
    // Codes are issued only for a display that asked for one, not for every visitor of the login page.
    if (!visible) return
    const controller = new AbortController()
    let timer: ReturnType<typeof setTimeout> | undefined
    let current: PairingRequest | undefined

    function schedule(delayMs: number) {
      if (controller.signal.aborted) return
      timer = setTimeout(run, delayMs)
    }

    async function run() {
      try {
        const step = nextPairingStep(current, Date.now())
        if (step.action === 'renew') {
          current = await createPairingRequest(controller.signal)
          setRequest(current)
          setError('')
          schedule(nextPairingStep(current, Date.now()).delayMs)
          return
        }

        const outcome = await claimPairingSession(current!, controller.signal)
        if (controller.signal.aborted) return
        if (outcome === 'approved') {
          navigate('/player', { replace: true })
          return
        }
        // A code the server no longer knows is replaced instead of polled forever.
        if (outcome !== 'pending') {
          current = undefined
          setRequest(undefined)
        }
        schedule(step.delayMs)
      } catch {
        if (controller.signal.aborted) return
        current = undefined
        setRequest(undefined)
        setError('Не удалось подготовить вход по QR-коду. Повторяем попытку…')
        schedule(10_000)
      }
    }

    void run()
    return () => {
      controller.abort()
      if (timer) clearTimeout(timer)
    }
  }, [navigate, visible])

  // Showing the code is the only gesture a display makes, and fullscreen needs one: the document
  // stays fullscreen through the route change to the player.
  function enterFullscreen() {
    if (document.fullscreenElement) return
    void document.documentElement.requestFullscreen?.().catch(() => {})
  }

  return (
    <section className="device-login" aria-labelledby="device-login-title">
      <h2 id="device-login-title">Вход с телефона</h2>
      <p>Отсканируйте код телефоном, где вы уже вошли, и подтвердите вход. Экран войдёт в вашу сессию сам.</p>
      <button type="button" className="secondary-button" aria-controls="device-login-code" aria-expanded={visible}
        onClick={() => { if (!visible) enterFullscreen(); setVisible(value => !value) }}>
        {visible ? 'Скрыть QR-код' : 'Показать QR-код'}
      </button>
      {visible && <>
        <div id="device-login-code" className="device-login-code">
          {request && <img src={request.imageUrl} alt="QR-код для входа с телефона" />}
          {!request && !error && <span role="status">Создаём QR-код…</span>}
        </div>
        {request && <p className="device-login-key">
          Код подтверждения: <strong>{formatUserCode(request.userCode)}</strong>
        </p>}
      </>}
      {error && <p className="auth-error" role="alert">{error}</p>}
    </section>
  )
}
