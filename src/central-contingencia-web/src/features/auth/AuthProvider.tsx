import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { api, ApiError } from '../../lib/api/client'
import { AuthContext, sessionEndedNotice, type User } from './auth-context'
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const refresh = useCallback(async () => {
    setError('')
    try { setUser(await api<User>('/auth/me')) }
    catch (reason) {
      if (reason instanceof ApiError && reason.status === 401) setUser(null)
      else setError('Não foi possível verificar a sessão. Verifique a conexão e tente novamente.')
    } finally { setLoading(false) }
  }, [])
  useEffect(() => {
    void refresh()
    const expired = () => { setUser(null); setNotice('Sessão expirada. Entre novamente.') }
    window.addEventListener('session-expired', expired)
    return () => window.removeEventListener('session-expired', expired)
  }, [refresh])
  // Detecta expiração também quando a aba volta a ficar ativa.
  useEffect(() => {
    if (!user) return
    const check = async () => {
      try { setUser(await api<User>('/auth/me')) }
      catch (reason) {
        if (reason instanceof ApiError && reason.status === 401) {
          setUser(null); setNotice('Sessão expirada. Entre novamente.')
        }
      }
    }
    window.addEventListener('focus', check)
    return () => window.removeEventListener('focus', check)
  }, [user])
  async function login(email: string, password: string) {
    await api('/auth/login', { email, password })
    setUser(await api<User>('/auth/me'))
    setNotice('')
  }
  async function logout() {
    await api('/auth/logout', {})
    setUser(null); setNotice(sessionEndedNotice)
  }
  return <AuthContext.Provider value={{ user, loading, error, notice, login, logout, refresh }}>{children}</AuthContext.Provider>
}
