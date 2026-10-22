export type ProxyStatus = 'Active' | 'Suspended' | 'Disabled'
export type ProxySort = 'host' | 'port' | 'region' | 'status' | 'expiresAt' | 'createdAt' | 'updatedAt'
export interface ProxyItem {
  id: string; host: string; port: number; usernameMasked: string; status: ProxyStatus; region: string;
  expiresAt: string; createdAt: string; updatedAt: string; available: boolean
}
export interface ProxyPage { items: ProxyItem[]; page: number; pageSize: number; total: number }
export interface ProxyInput { host: string; port: number; username: string; password: string; region: string; status: ProxyStatus; expiresAt: string }
export type ProxyPatch = Partial<ProxyInput>
export interface ProxyFilters { host?: string; port?: number; region?: string; status?: ProxyStatus; expiresBefore?: string }
export interface ProxyListOptions extends ProxyFilters { page: number; pageSize: number; sort: ProxySort; direction: 'asc' | 'desc'; availableOnly: boolean }
export const statusLabels: Record<ProxyStatus, string> = { Active: 'Ativo', Suspended: 'Suspenso', Disabled: 'Desativado' }
