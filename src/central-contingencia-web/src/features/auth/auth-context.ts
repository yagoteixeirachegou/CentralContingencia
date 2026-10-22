import { createContext, useContext } from 'react'
export interface User { id: string; email: string; roles: string[] }
export interface AuthState {
  user: User | null; loading: boolean; error: string; notice: string
  login(email: string, password: string): Promise<void>
  logout(): Promise<void>
  refresh(): Promise<void>
}
export const sessionEndedNotice = 'Sessão encerrada.'
export const AuthContext = createContext<AuthState | null>(null)
export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) throw new Error('AuthProvider ausente')
  return context
}
