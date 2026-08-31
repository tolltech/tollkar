import type { User } from './api'

export function canAccessAdmin(user: Pick<User, 'isAdmin'>) {
  return user.isAdmin
}
