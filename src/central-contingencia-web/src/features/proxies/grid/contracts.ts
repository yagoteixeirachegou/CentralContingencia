import type { ProxyStatus } from '../contracts'
export const assignments = ['DARKPOST', 'MODERAÇÃO', 'ANUNCIANTE', 'HOST PÁGINAS', 'FLAG', 'BACKUP PIXEL', 'ESTOURADO', 'RESTRITA', 'BUGADO', 'ATIVO (LIVRE)', 'ESTEIRA DE PERFIL', 'MINERAÇÃO', 'FLAG (LIVRE)', 'TIKTOK', 'BM', 'X1', 'GOOGLE ADS', 'INFOPRODUTO'] as const
export type Assignment = typeof assignments[number]
export type Availability = 'Available' | 'Reserved' | 'InUse' | 'Unavailable'
export const availabilityLabels: Record<Availability, string> = { Available: 'Disponível', Reserved: 'Reservada', InUse: 'Em uso', Unavailable: 'Indisponível' }
export interface GridRow {
  proxyId: string; profileId: string | null; profileName: string | null
  platform1: string | null; platform2: string | null; host: string; port: number; region: string
  expiresAt: string; status: ProxyStatus; availability: Availability; allocationId: string | null; reservationExpiresAt: string | null
}
export interface GridOptions {
  page: number; pageSize: number; profile?: string; region?: string; platform?: Assignment
  availability?: Availability; sort: 'expiresAt'; direction: 'asc' | 'desc'
}
export interface GridPage { items: GridRow[]; page: number; pageSize: number; total: number; evaluatedAt: string }
