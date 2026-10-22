export type ProfileStatus = 'Active' | 'Closed'
export type ProfileSort = 'name' | 'platform' | 'status' | 'createdAt' | 'updatedAt'
export interface OperationalProfile {
  id: string; name: string; platform: string; status: ProfileStatus
  createdAt: string; updatedAt: string; closedAt: string | null
  platform1?: string | null; platform2?: string | null
}
export interface ProfileInput { name: string; platform?: string; platform1?: string | null; platform2?: string | null }
export interface Page<T> { items: T[]; page: number; pageSize: number; total: number }
export interface ProfileOptions {
  page: number; pageSize: number; name?: string; platform?: string; status?: ProfileStatus
  sort: ProfileSort; direction: 'asc' | 'desc'
}
export type AllocationStatus = 'Reserved' | 'Confirmed' | 'Released' | 'Expired'
export interface Allocation {
  id: string; profileId: string; proxyId: string; status: AllocationStatus
  createdAt: string; updatedAt: string; reservationExpiresAt: string
  confirmedAt: string | null; endedAt: string | null
  endReason: 'Released' | 'ProfileClosed' | 'ReservationExpired' | null
}
export interface AllocationOptions {
  page: number; pageSize: number; status?: AllocationStatus
  sort: 'createdAt' | 'updatedAt' | 'status'; direction: 'asc' | 'desc'
}
export const profileLabels: Record<ProfileStatus, string> = { Active: 'Ativo', Closed: 'Encerrado' }
export const allocationLabels: Record<AllocationStatus, string> = { Reserved: 'Reservada', Confirmed: 'Confirmada', Released: 'Liberada', Expired: 'Expirada' }
export const endLabels = { Released: 'Liberação', ProfileClosed: 'Perfil encerrado', ReservationExpired: 'Reserva expirada' }
export const profileRead = (roles: string[] = []) => roles.some(role => ['Admin', 'Manager', 'Operator'].includes(role))
export const profileWrite = (roles: string[] = []) => roles.some(role => ['Admin', 'Manager'].includes(role))
export function dateLabel(value: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isFinite(date.getTime()) ? date.toLocaleString('pt-BR') : 'Data indisponível'
}
