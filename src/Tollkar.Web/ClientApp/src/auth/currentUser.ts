import { createContext, useContext } from 'react'
import type { User } from './api'

export const CurrentUserContext = createContext<User | undefined>(undefined)

export function useCurrentUser() {
  const user = useContext(CurrentUserContext)
  if (!user) throw new Error('Authenticated user context is required.')
  return user
}
