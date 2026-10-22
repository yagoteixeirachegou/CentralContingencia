import { api, ApiError } from '../../lib/api/client'
import type { QueryClient } from '@tanstack/react-query'
import type { Allocation, AllocationOptions, OperationalProfile, Page, ProfileInput, ProfileOptions } from './contracts'
function query(values: object) {
  const params = new URLSearchParams()
  for (const [name, value] of Object.entries(values)) if (value !== undefined && value !== '') params.set(name, String(value))
  return params.toString()
}
export const listProfiles = (options: ProfileOptions, signal?: AbortSignal) => api<Page<OperationalProfile>>(`/profiles?${query(options)}`, undefined, { signal })
export const getProfile = (id: string, signal?: AbortSignal) => api<OperationalProfile>(`/profiles/${encodeURIComponent(id)}`, undefined, { signal })
export const createProfile = (input: ProfileInput) => api<OperationalProfile>('/profiles', input)
// PATCH: omitted slots are preserved by the API; null clears the slot.
export const updateProfileAssignments = (id: string, input: Pick<ProfileInput, 'platform1' | 'platform2'>) => api<OperationalProfile>(`/profiles/${encodeURIComponent(id)}`, input, { method: 'PATCH' })
export const profileHistory = (id: string, options: AllocationOptions, signal?: AbortSignal) => api<Page<Allocation>>(`/profiles/${encodeURIComponent(id)}/allocations?${query(options)}`, undefined, { signal })
export async function currentAllocation(id: string, signal?: AbortSignal): Promise<Allocation | null> {
  const results = await Promise.all((['Reserved', 'Confirmed'] as const).map(status =>
    api<Page<Allocation>>(`/allocations?${query({ profileId: id, status, page: 1, pageSize: 1, sort: 'createdAt', direction: 'desc' })}`, undefined, { signal })))
  // The API does not provide a single current-allocation endpoint. Read both states
  // independently of the history page; transient inconsistent reads require refresh.
  if (results.some(page => page.total > 1)) throw new ApiError(409, 'Atualize a alocação.')
  const items = new Map(results.flatMap(page => page.items).map(item => [item.id, item]))
  if (items.size > 1) throw new ApiError(409, 'Atualize a alocação.')
  return [...items.values()][0] ?? null
}
export const reserveProxy = (profileId: string, proxyId: string, key: string) => api<Allocation>('/allocations/reserve', { profileId, proxyId }, { headers: { 'Idempotency-Key': key } })
export const confirmAllocation = (id: string) => api<void>(`/allocations/${encodeURIComponent(id)}/confirm`, {})
export const releaseAllocation = (id: string) => api<void>(`/allocations/${encodeURIComponent(id)}/release`, {})
export const refreshOperations = (client: QueryClient) => Promise.all(['profiles', 'allocations', 'proxies'].map(name => client.invalidateQueries({ queryKey: [name] })))
export function operationalError(error: unknown) {
  if (error instanceof ApiError) {
    if (error.status === 401) return 'Sessão expirada. Entre novamente.'
    if (error.status === 403) return 'Acesso negado. Seu usuário não tem permissão para esta operação.'
    if (error.status === 404) return 'Perfil, proxy ou alocação não encontrado. Atualize os dados.'
    if (error.status === 409) return 'Conflito operacional: o estado mudou, a reserva pode ter expirado ou o recurso já estar ocupado. Atualize e confira o estado antes de tentar novamente.'
    if (error.status === 400) return 'Verifique os campos. A proteção CSRF pode precisar ser renovada.'
    if (error.status === 429) return 'Limite de requisições atingido. Aguarde um minuto.'
    if (error.status === 500) return 'Falha interna ao registrar a operação. Atualize os dados antes de tentar novamente.'
    if (error.status === 503) return 'Serviço indisponível. Atualize os dados antes de tentar novamente.'
  }
  return 'Não foi possível confirmar o resultado. Atualize os dados antes de tentar novamente.'
}
