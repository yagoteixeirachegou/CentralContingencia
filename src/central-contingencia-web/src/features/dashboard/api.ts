import { z } from 'zod'
import { api, ApiError } from '../../lib/api/client'
const when = z.string().datetime({ offset: true }).nullable()
const groupSchema = z.object({ groupId: z.string(), groupName: z.string(), total: z.number().int(), oldestCreatedAt: when, newestCreatedAt: when })
const tagSchema = z.object({ name: z.string(), total: z.number().int() })
const profileSchema = z.object({ profileId: z.string(), serialNumber: z.string().nullable(), name: z.string(), groupId: z.string(), groupName: z.string(), tags: z.array(z.string()), createdAt: when })
const dashboardSchema = z.object({ generatedAt: z.string().datetime({ offset: true }), total: z.number().int(), groups: z.array(groupSchema), tags: z.array(tagSchema), profiles: z.array(profileSchema), fromCache: z.boolean() })
export type AdsPowerDashboard = z.infer<typeof dashboardSchema>
export type AdsPowerGroup = z.infer<typeof groupSchema>
export type AdsPowerTag = z.infer<typeof tagSchema>
export type AdsPowerProfile = z.infer<typeof profileSchema>
function parse(value: unknown): AdsPowerDashboard {
  const result = dashboardSchema.safeParse(value)
  if (!result.success) throw new ApiError(502, 'Resposta do dashboard incompatível. Atualize os dados.')
  return result.data
}
export const getAdsPowerDashboard = async (signal?: AbortSignal) => parse(await api<unknown>('/adspower/dashboard', undefined, { signal, readProblem: true }))
// Nova leitura do AdsPower, ignorando o resultado em memória (Admin e Manager).
export const refreshAdsPowerDashboard = async () => parse(await api<unknown>('/adspower/dashboard/refresh', {}, { readProblem: true }))
// A API devolve um título já sanitizado para falhas do AdsPower; os demais erros usam textos genéricos.
export function dashboardError(error: unknown) {
  if (error instanceof ApiError) {
    const title = (error.problem as { title?: unknown } | undefined)?.title
    if ((error.status === 502 || error.status === 503) && typeof title === 'string') return title
    if (error.status === 403) return 'Acesso negado. Seu usuário não tem permissão para esta operação.'
    if (error.status === 429) return 'Limite de requisições atingido. Aguarde um minuto.'
    if (error.status === 401) return 'Sessão expirada. Entre novamente.'
  }
  return 'Não foi possível carregar o dashboard. Tente novamente.'
}
