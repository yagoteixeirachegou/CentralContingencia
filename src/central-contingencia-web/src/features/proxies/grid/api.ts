import { api, ApiError } from '../../../lib/api/client'
import { z } from 'zod'
import type { GridOptions, GridPage } from './contracts'
const rowSchema = z.object({ proxyId: z.string(), profileId: z.string().nullable(), profileName: z.string().nullable(), platform1: z.string().nullable(), platform2: z.string().nullable(),
  host: z.string(), port: z.number(), region: z.string(), expiresAt: z.string().datetime({ offset: true }), status: z.enum(['Active', 'Suspended', 'Disabled']),
  availability: z.enum(['Available', 'Reserved', 'InUse', 'Unavailable']), allocationId: z.string().nullable(), reservationExpiresAt: z.string().datetime({ offset: true }).nullable() })
const pageSchema = z.object({ items: z.array(rowSchema), page: z.number(), pageSize: z.number(), total: z.number(), evaluatedAt: z.string().datetime({ offset: true }) })
export async function listOperationalGrid(options: GridOptions, signal?: AbortSignal): Promise<GridPage> {
  const query = new URLSearchParams()
  for (const [key, value] of Object.entries(options)) if (value !== undefined && value !== '') query.set(key, String(value))
  const result = pageSchema.safeParse(await api<unknown>(`/proxies/operational-grid?${query}`, undefined, { signal }))
  if (!result.success) throw new ApiError(503, 'Resposta operacional incompatível. Atualize os dados.')
  return result.data
}
