import { api, ApiError } from '../../lib/api/client'
import type { ProxyInput, ProxyItem, ProxyListOptions, ProxyPage, ProxyPatch } from './contracts'
export function listProxies(options: ProxyListOptions, signal?: AbortSignal) {
  const { availableOnly, ...values } = options
  const query = new URLSearchParams()
  for (const [key, value] of Object.entries(values)) if (value !== undefined && value !== '') query.set(key, String(value))
  return api<ProxyPage>(`/proxies${availableOnly ? '/available' : ''}?${query}`, undefined, { signal })
}
export const getProxy = (id: string, signal?: AbortSignal) => api<ProxyItem>(`/proxies/${encodeURIComponent(id)}`, undefined, { signal })
export const createProxy = (body: ProxyInput) => api<ProxyItem>('/proxies', body)
export const updateProxy = (id: string, body: ProxyPatch) => api<ProxyItem>(`/proxies/${encodeURIComponent(id)}`, body, { method: 'PATCH' })
export function proxyError(error: unknown) {
  if (error instanceof ApiError) {
    if (error.status === 401) return 'Sessão expirada. Entre novamente.'
    if (error.status === 400) return 'Verifique os campos e tente novamente. A sessão CSRF pode precisar ser renovada.'
    if (error.status === 404) return 'Este proxy não foi encontrado. Atualize a listagem.'
    if (error.status === 409) return 'O registro mudou ou houve um conflito. Atualize a listagem antes de tentar novamente.'
    if (error.status === 503) return 'Serviço de proxies indisponível. Tente novamente mais tarde.'
    return error.message
  }
  return 'Não foi possível conectar à API. Verifique a conexão e tente novamente.'
}
export interface ProxyCredentials { id: string; host: string; port: number; username: string; password: string }
// Revelação sob demanda (Admin, auditada): nunca guardar o resultado em cache de consulta.
export const revealProxyCredentials = (id: string) => api<ProxyCredentials>(`/proxies/${encodeURIComponent(id)}/credentials/reveal`, {})
