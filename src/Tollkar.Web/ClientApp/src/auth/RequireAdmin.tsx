import { Navigate, Outlet } from 'react-router-dom'
import { canAccessAdmin } from './authorization'
import { useCurrentUser } from './currentUser'

export function RequireAdmin() {
  const user = useCurrentUser()
  return canAccessAdmin(user) ? <Outlet /> : <Navigate to="/queue" replace />
}
