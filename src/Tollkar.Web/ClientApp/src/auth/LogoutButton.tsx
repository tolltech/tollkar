import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { submitAuth } from './api'

/** A display hides the header, so the only way out of its session travels with this button. */
export function LogoutButton() {
  const navigate = useNavigate()
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')

  async function logout() {
    setPending(true)
    setError('')
    try {
      await submitAuth('logout')
      navigate('/login', { replace: true })
    } catch {
      setError('Не удалось выйти. Повторите попытку.')
    } finally {
      setPending(false)
    }
  }

  return <>
    <button type="button" className="secondary-button" disabled={pending} onClick={() => void logout()}>
      {pending ? 'Выходим…' : 'Выйти'}
    </button>
    {error && <p className="auth-error" role="alert">{error}</p>}
  </>
}
