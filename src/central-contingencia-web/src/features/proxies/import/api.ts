import { api, ApiError } from '../../../lib/api/client'
import { reportSchema, type CsvPreview, type CsvReport } from './contracts'
function form(file: File) { const body = new FormData(); body.append('file', file, 'import.csv'); return body }
function path(operation: string, region: string, page?: number, skipRowsWithoutConnection = false, referenceYear = '') {
  const query = new URLSearchParams()
  if (region !== '') query.set('defaultRegion', region)
  if (skipRowsWithoutConnection) query.set('skipRowsWithoutConnection', 'true')
  if (referenceYear !== '') query.set('referenceYear', referenceYear)
  if (page !== undefined) { query.set('page', String(page)); query.set('pageSize', '100') }
  return `/proxies/import/${operation}?${query}`
}
export const previewCsv = (file: File, region: string, page: number, signal?: AbortSignal, skipRowsWithoutConnection = false, referenceYear = '') => api<CsvPreview>(path('preview', region, page, skipRowsWithoutConnection, referenceYear), form(file), { signal })
export const commitCsv = (file: File, region: string, proof: string, key: string, signal?: AbortSignal, skipRowsWithoutConnection = false, referenceYear = '') => api<CsvReport>(path('commit', region, undefined, skipRowsWithoutConnection, referenceYear), form(file), {
  signal, readProblem: true, headers: { 'X-Import-Confirmation': proof, 'Idempotency-Key': key },
})
export function failureReport(error: unknown) {
  if (!(error instanceof ApiError) || error.status !== 422 || typeof error.problem !== 'object' || error.problem === null || !('report' in error.problem)) return undefined
  const parsed = reportSchema.safeParse(error.problem.report)
  return parsed.success ? parsed.data : undefined
}
export function importError(error: unknown) {
  if (!(error instanceof ApiError)) return 'Não foi possível confirmar a resposta da API. Tente novamente com o mesmo arquivo; a chave da operação será mantida.'
  const messages: Record<number, string> = {
    400: 'Arquivo vazio/malformado, região ou ano inválidos, ou proteção CSRF recusada. Revise os dados e tente novamente.',
    401: 'Sessão expirada. Entre novamente.', 403: 'Acesso negado. Somente administradores podem importar CSV.',
    409: 'A prova expirou ou o arquivo, região, ano ou identidade mudou. Gere uma nova prévia; a chave da operação será mantida.',
    413: 'O arquivo excede os limites da API.', 415: 'Formato de envio não aceito.',
    422: 'Nenhum registro foi gravado. Corrija as linhas inválidas e gere nova prévia.',
    429: 'Limite de requisições atingido. Aguarde um minuto antes de tentar novamente.',
    503: 'Serviço indisponível. Repita com a mesma operação; não haverá reenvio automático.',
  }
  return messages[error.status] ?? 'Não foi possível concluir a operação. Tente novamente; a chave será mantida.'
}
