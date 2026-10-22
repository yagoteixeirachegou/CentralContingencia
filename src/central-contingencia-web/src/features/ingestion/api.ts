import { api, ApiError } from '../../lib/api/client'

export interface IngestionStatus {
  configured: boolean; inProgress: boolean; availableProxies: number; nextProfileNumber: number; nextProfileName: string
  group: { id: string; name: string; mode: 'Test' | 'Production' }; maxFiles: number; maxFileBytes: number
}
export interface UploadReport {
  title: string; message: string; groupName: string | null; environment: string | null; failed: boolean
  summary: { requested: number; reserved: number; created: number; adsPowerFailures: number; unfilledForLackOfProxy: number; filesForReview: number } | null
  created: { profile: string; adsPowerId: string | null; serial: string | null; confirmedInCentral: boolean; warning: string | null }[]
  failures: { profile: string; reason: string | null; adsPowerMessage: string | null; proxyReleased: boolean }[]
  unfilled: { file: string | null; reason: string | null; message: string | null }[]
  review: { file: string; reasons: string[] }[]
  reservationsUndone: number | null
}

export const getStatus = (signal?: AbortSignal) => api<IngestionStatus>('/profile-ingestions/status', undefined, { signal, readProblem: true })

// Os arquivos têm credenciais: seguem direto para a API, sem armazenamento no navegador.
export function submitFiles(files: File[], signal?: AbortSignal) {
  const body = new FormData()
  for (const file of files) body.append('files', file, file.name)
  return api<UploadReport>('/profile-ingestions/submit', body, { signal, readProblem: true })
}

// As mensagens da API já são sanitizadas e escritas para o usuário; usa-se o título quando existe.
export function uploadError(error: unknown) {
  if (error instanceof ApiError && typeof error.problem === 'object' && error.problem !== null && 'title' in error.problem && typeof error.problem.title === 'string' && error.status !== 401)
    return error.problem.title
  if (error instanceof ApiError) return error.message
  return 'Não foi possível confirmar a resposta da Central. Confira o resultado em Proxies antes de reenviar; reservas não usadas voltam sozinhas em até 15 minutos.'
}

export function validateFiles(files: File[], status: Pick<IngestionStatus, 'maxFiles' | 'maxFileBytes'>) {
  if (files.length > status.maxFiles) return `No máximo ${status.maxFiles} arquivos por envio.`
  const names = new Set<string>()
  for (const file of files) {
    if (!file.name.toLowerCase().endsWith('.txt')) return `${file.name}: somente arquivos .txt.`
    if (file.size === 0) return `${file.name}: arquivo vazio.`
    if (file.size > status.maxFileBytes) return `${file.name}: acima de ${Math.round(status.maxFileBytes / 1024)} KB.`
    const key = file.name.toLowerCase()
    if (names.has(key)) return `${file.name}: nome repetido.`
    names.add(key)
  }
  return ''
}
