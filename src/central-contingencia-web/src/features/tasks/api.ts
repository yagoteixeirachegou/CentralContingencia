import { z } from 'zod'
import { api, ApiError } from '../../lib/api/client'

const when = z.string().datetime({ offset: true })
const taskSchema = z.object({
  id: z.string().uuid(), type: z.string(), status: z.enum(['Pending', 'Running', 'Succeeded', 'Failed', 'Cancelled', 'DeadLetter']),
  createdAt: when, startedAt: when.nullable(), completedAt: when.nullable(), nextAttemptAt: when.nullable(), lastActivityAt: when,
  attemptCount: z.number().int(), maxAttempts: z.number().int(), lastErrorCode: z.string().nullable(), correlationId: z.string(),
  cancelRequested: z.boolean(), executorId: z.string().uuid().nullable(), executorName: z.string().nullable(),
})
const pageSchema = z.object({ items: z.array(taskSchema), page: z.number().int(), pageSize: z.number().int(), total: z.number().int() })
const executorSchema = z.object({
  id: z.string().uuid(), name: z.string(), status: z.enum(['Active', 'Revoked']), connection: z.enum(['Online', 'Offline', 'Revoked']),
  state: z.enum(['Offline', 'Idle', 'Busy', 'Draining']), lastSeenAt: when.nullable(), workerVersion: z.string().nullable(),
  allowedTypes: z.array(z.string()), createdAt: when, credentialRotatedAt: when.nullable(), revokedAt: when.nullable(), runningTasks: z.number().int(),
})
const provisionedSchema = z.object({ executor: executorSchema, credential: z.string() })

export type WorkTask = z.infer<typeof taskSchema>
export type TaskPage = z.infer<typeof pageSchema>
export type TaskStatus = WorkTask['status']
export type Executor = z.infer<typeof executorSchema>
export type ProvisionedExecutor = z.infer<typeof provisionedSchema>
export interface TaskQuery { page: number; pageSize: number; status?: TaskStatus }

function parse<T>(schema: z.ZodType<T>, value: unknown): T {
  const result = schema.safeParse(value)
  if (!result.success) throw new ApiError(502, 'Resposta incompatível com o esperado. Atualize a página.')
  return result.data
}

export const listTasks = async (query: TaskQuery, signal?: AbortSignal) => {
  const params = new URLSearchParams({ page: String(query.page), pageSize: String(query.pageSize) })
  if (query.status) params.set('status', query.status)
  return parse(pageSchema, await api<unknown>(`/tasks?${params}`, undefined, { signal }))
}
export const cancelTask = async (id: string) => parse(taskSchema, await api<unknown>(`/tasks/${id}/cancel`, {}))
export const listExecutors = async (signal?: AbortSignal) => parse(z.array(executorSchema), await api<unknown>('/executors', undefined, { signal }))
export const provisionExecutor = async (name: string) => parse(provisionedSchema, await api<unknown>('/executors', { name }, { readProblem: true }))
export const rotateExecutor = async (id: string) => parse(provisionedSchema, await api<unknown>(`/executors/${id}/rotate`, {}))
export const revokeExecutor = async (id: string) => parse(executorSchema, await api<unknown>(`/executors/${id}/revoke`, {}))

export const statusLabel: Record<TaskStatus, string> = {
  Pending: 'Na fila', Running: 'Em execução', Succeeded: 'Concluída', Failed: 'Falhou', Cancelled: 'Cancelada', DeadLetter: 'Esgotada (dead-letter)',
}
export const connectionLabel: Record<Executor['connection'], string> = { Online: 'Online', Offline: 'Offline', Revoked: 'Revogado' }
export const stateLabel: Record<Executor['state'], string> = { Offline: 'Offline', Idle: 'Ocioso', Busy: 'Ocupado', Draining: 'Encerrando' }

export function taskError(reason: unknown, fallback = 'Não foi possível concluir a solicitação.') {
  if (reason instanceof ApiError) {
    const title = (reason.problem as { title?: unknown } | undefined)?.title
    if (reason.status === 409) return typeof title === 'string' ? title : 'Conflito: o estado mudou. Atualize a lista.'
    return reason.message
  }
  return fallback
}
