import { z } from 'zod'
import type { ProxyInput, ProxyItem, ProxyPatch } from './contracts'
const text = (max: number) => z.string().max(max, `Use até ${max} caracteres.`).refine(v => v.trim().length > 0 && ![...v].some(char => { const code = char.charCodeAt(0); return code <= 31 || (code >= 127 && code <= 159) }), 'Informe um valor válido, sem caracteres de controle.')
function hostValid(value: string) {
  if (value.length > 253 || value !== value.trim() || /[\s/@?#%]/.test(value) || value.includes('[') || value.includes(']')) return false
  if (value.includes(':')) {
    try { return new URL(`http://[${value}]/`).hostname.startsWith('[') } catch { return false }
  }
  if (/^[\d.]+$/.test(value) || value.split('.').every(part => /^(?:\d+|0x[a-f\d]+)$/i.test(part))) return value.split('.').length === 4 && value.split('.').every(part => /^(0|[1-9]\d{0,2})$/.test(part) && Number(part) <= 255)
  return value.split('.').every(label => /^[a-z\d](?:[a-z\d-]{0,61}[a-z\d])?$/i.test(label))
}
export const proxySchema = (editing: boolean) => z.object({
  host: z.string().refine(hostValid, 'Informe DNS, IPv4 ou IPv6 válido, sem URL ou porta.'),
  port: z.string().regex(/^\d+$/, 'Informe uma porta inteira.').refine(v => Number(v) >= 1 && Number(v) <= 65535, 'Use uma porta entre 1 e 65535.'),
  username: editing ? z.string().refine(v => v === '' || text(128).safeParse(v).success, 'Use até 128 caracteres válidos.') : text(128),
  password: z.string().refine(v => (editing && v === '') || (v.length > 0 && new TextEncoder().encode(v).length <= 1024 && !/[\ud800-\udbff](?![\udc00-\udfff])|(?<![\ud800-\udbff])[\udc00-\udfff]/.test(v)), 'Informe uma senha de 1 a 1024 bytes UTF-8.'),
  region: text(100), status: z.enum(['Active', 'Suspended', 'Disabled']),
  expiresAt: z.string().refine(v => Number.isFinite(new Date(v).getTime()), 'Informe a data e hora de vencimento.'),
})
export type ProxyFormValues = z.infer<ReturnType<typeof proxySchema>>
export function localDate(value: string) {
  const date = new Date(value)
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000)
  return local.toISOString().slice(0, 16)
}
export function formDefaults(proxy?: ProxyItem): ProxyFormValues {
  return { host: proxy?.host ?? '', port: proxy ? String(proxy.port) : '', username: '', password: '', region: proxy?.region ?? '',
    status: proxy?.status ?? 'Disabled', expiresAt: proxy ? localDate(proxy.expiresAt) : '' }
}
export function formPayload(values: ProxyFormValues, original?: ProxyItem): ProxyInput | ProxyPatch {
  if (!original) return { ...values, port: Number(values.port), expiresAt: new Date(values.expiresAt).toISOString() }
  const changes: ProxyPatch = {}
  if (values.host !== original.host) changes.host = values.host
  if (Number(values.port) !== original.port) changes.port = Number(values.port)
  if (values.region !== original.region) changes.region = values.region
  if (values.status !== original.status) changes.status = values.status
  // Não arredondar segundos/microssegundos de um vencimento que não foi editado.
  if (values.expiresAt !== localDate(original.expiresAt)) changes.expiresAt = new Date(values.expiresAt).toISOString()
  if (values.username !== '') changes.username = values.username
  if (values.password !== '') changes.password = values.password
  return changes
}
