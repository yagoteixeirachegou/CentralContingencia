import { useEffect, useRef, useState } from 'react'
import { Check, Copy, Eye, EyeOff } from 'lucide-react'
import { Button } from '../../components/ui/button'
import { useAuth } from '../auth/auth-context'
import { proxyError, revealProxyCredentials, type ProxyCredentials } from './api'

const hideAfterMs = 30_000
const dots = '••••••••••'

// Usuário e senha ficam ocultos; o olho revela (uma consulta auditada no servidor) e "Copiar tudo" copia host:porta:usuário:senha.
// O valor vive só neste componente: some ao ocultar, após 30 s, ao trocar de proxy ou ao fechar.
export function ProxyCredentialsPanel({ proxyId, host, port, showEndpoint = true }: { proxyId: string; host: string; port: number; showEndpoint?: boolean }) {
  const { user } = useAuth()
  const [credentials, setCredentials] = useState<ProxyCredentials | null>(null)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  const [copied, setCopied] = useState(false)
  const timer = useRef<number | undefined>(undefined)
  const copiedTimer = useRef<number | undefined>(undefined)
  const clear = () => { window.clearTimeout(timer.current); setCredentials(null) }
  useEffect(() => { clear(); setError(''); setCopied(false) }, [proxyId])
  useEffect(() => () => { window.clearTimeout(timer.current); window.clearTimeout(copiedTimer.current) }, [])
  if (!user?.roles.includes('Admin')) return <p className="text-sm">Usuário e senha: somente Admin</p>

  async function load() {
    if (credentials) return credentials
    setPending(true); setError('')
    try {
      const loaded = await revealProxyCredentials(proxyId)
      setCredentials(loaded)
      window.clearTimeout(timer.current); timer.current = window.setTimeout(() => setCredentials(null), hideAfterMs)
      return loaded
    } catch (reason) { setError(proxyError(reason)); return null }
    finally { setPending(false) }
  }
  async function copyAll() {
    const loaded = await load(); if (!loaded) return
    try {
      await navigator.clipboard.writeText(`${loaded.host}:${loaded.port}:${loaded.username}:${loaded.password}`)
      setCopied(true); window.clearTimeout(copiedTimer.current); copiedTimer.current = window.setTimeout(() => setCopied(false), 3000)
    } catch { setError('Não foi possível copiar. Mostre os dados e copie manualmente.') }
  }
  const shown = credentials !== null
  return <div className="space-y-2 rounded-lg border border-slate-200 bg-slate-50 p-3 text-sm" aria-label="Credenciais da proxy">
    <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
      {showEndpoint && <><dt className="text-slate-600">Proxy</dt><dd className="break-all">{host}:{port}</dd></>}
      <dt className="text-slate-600">Usuário</dt><dd className="break-all font-mono" data-testid="proxy-username">{shown ? credentials.username : dots}</dd>
      <dt className="text-slate-600">Senha</dt><dd className="break-all font-mono" data-testid="proxy-password">{shown ? credentials.password : dots}</dd>
    </dl>
    <div className="flex flex-wrap gap-2">
      <Button variant="outline" className="gap-2 px-3 py-1.5 text-sm" disabled={pending} aria-pressed={shown}
        onClick={() => { if (shown) clear(); else void load() }}>
        {shown ? <EyeOff size={16} aria-hidden="true" /> : <Eye size={16} aria-hidden="true" />}{shown ? 'Ocultar' : pending ? 'Consultando…' : 'Mostrar usuário e senha'}</Button>
      <Button variant="outline" className="gap-2 px-3 py-1.5 text-sm" disabled={pending} onClick={() => void copyAll()}>
        {copied ? <Check size={16} aria-hidden="true" /> : <Copy size={16} aria-hidden="true" />}{copied ? 'Copiado' : 'Copiar tudo'}</Button>
    </div>
    <p className="text-xs text-slate-600">Cada consulta fica registrada na auditoria. Os dados somem da tela em 30 segundos.</p>
    {error && <p role="alert" className="text-xs text-red-700">{error}</p>}
    <p role="status" className="sr-only">{copied ? 'Credenciais copiadas.' : ''}</p>
  </div>
}
