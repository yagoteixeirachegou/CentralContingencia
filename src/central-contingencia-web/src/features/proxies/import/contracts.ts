import { z } from 'zod'
export interface CsvIssue { line: number; column: number | null; code: string; message: string }
export interface CsvRow {
  line: number; endLine: number; hostMasked: string | null; port: number | null
  status: string | null; expiresAt: string | null; regionPresent: boolean; valid: boolean; duplicate: boolean; existingConflict: boolean
  errors: CsvIssue[]; warnings: CsvIssue[]
}
export interface CsvPreview {
  columns: { position: number; field: string | null }[]; headerIssues: CsvIssue[]
  summary: { total: number; valid: number; invalid: number; duplicates: number; empty: number; conflicts: number; warnings: number }
  page: number; pageSize: number; items: CsvRow[]; confirmationToken: string | null; confirmationExpiresAt: string | null
}
export const reportSchema = z.object({
  importId: z.string().uuid().nullable(), replayed: z.boolean(), total: z.number().int().nonnegative(),
  imported: z.number().int().nonnegative(), ignored: z.number().int().nonnegative(), invalid: z.number().int().nonnegative(), conflicting: z.number().int().nonnegative(),
  items: z.array(z.object({ line: z.number().int().min(2), proxyId: z.string().uuid().nullable(), outcome: z.enum(['Imported', 'Empty', 'Duplicate', 'Conflict', 'Invalid', 'NotImported']) })),
})
export type CsvReport = z.infer<typeof reportSchema>
export const importFormSchema = z.object({ defaultRegion: z.string(), skipRowsWithoutConnection: z.boolean(),
  referenceYear: z.string().refine(value => value === '' || (/^[0-9]+$/.test(value) && Number(value) >= 1 && Number(value) <= 9999), 'Informe um ano inteiro entre 1 e 9999 ou deixe vazio.'),
})
export type ImportForm = z.infer<typeof importFormSchema>
export const maxCsvBytes = 2 * 1024 * 1024
export const pageSize = 100
export function fileError(file: File | undefined) {
  return !file ? 'Selecione um arquivo CSV.' : !/\.csv$/i.test(file.name) ? 'Selecione um arquivo com extensão .csv.'
    : file.size === 0 ? 'O arquivo CSV está vazio.' : file.size > maxCsvBytes ? 'O limite inicial é 2 MiB. Selecione um arquivo menor.' : ''
}
export const outcomeLabels: Record<CsvReport['items'][number]['outcome'], string> = {
  Imported: 'Importado', Empty: 'Linha vazia ou sem conexão ignorada', Duplicate: 'Duplicata ignorada', Conflict: 'Conflito ignorado', Invalid: 'Inválido', NotImported: 'Não importado',
}
export function issueText(issue: CsvIssue, columns: CsvPreview['columns'] = []) {
  // Mensagens por código: não ecoar células ou mensagens arbitrárias.
  const messages: Record<string, string> = {
    MissingHeader: 'Cabeçalho obrigatório ausente.', DuplicateHeader: 'Cabeçalho duplicado.', UnknownHeader: 'Coluna desconhecida ignorada.',
    InvalidHost: 'Host inválido.', InvalidPort: 'Porta inválida.', InvalidStatus: 'Status inválido.', InvalidExpiry: 'Data de vencimento inválida ou vencida.',
    InvalidRegion: 'Região obrigatória ou inválida.', DuplicateRecord: 'Registro duplicado no arquivo.', EmptyRow: 'Linha vazia.',
    ExistingConflict: 'Proxy já cadastrado; será ignorado.', MissingRegion: 'Informe uma região explícita ou inclua REGIÃO no CSV.',
    ExplicitDefaultRegion: 'Região complementar explícita aplicada.', InvalidUsername: 'Usuário obrigatório ou inválido.', InvalidPassword: 'Credencial obrigatória ou inválida.',
    ExistingEndpoint: 'Proxy já cadastrado; será ignorado.', RegionRequiredByModel: 'Informe uma região explícita ou inclua REGIÃO no CSV.',
    UnmappedMetadata: 'PERFIL, PLATAFORMA e ADsPower Lote não serão importados nem vinculados aos proxies.', FormulaLikeText: 'Texto semelhante a fórmula não será executado.',
    IgnoredEmptyDuplicateHeader: 'Coluna duplicada totalmente vazia ignorada; numeração preservada.',
    NoConnectionRowSkipped: 'Linha sem host, porta, usuário e senha ignorada por opção explícita.',
    ExpiryYearRequired: 'Informe Ano de referência ou complete a data no CSV (dd/MM/yyyy); nenhum ano será inferido.',
    ReferenceYearApplied: 'Ano de referência explícito aplicado. Confira o vencimento interpretado.',
    InvalidHeaders: 'Corrija os cabeçalhos obrigatórios.', ColumnCount: 'Quantidade de campos diferente do cabeçalho.',
  }
  if (issue.code === 'InvalidField') {
    const field = columns.find(column => column.position === issue.column)?.field
    messages.InvalidField = field === 'DATA DE VENCIMENTO' ? 'Data de vencimento inválida ou vencida.'
      : field === 'REGIAO' || issue.column === null ? 'Região obrigatória ou inválida.'
      : field === 'SENHA' || field === 'USUARIO' ? 'Credencial obrigatória ou inválida.' : `Campo ${field ?? 'obrigatório'} inválido.`
  }
  if (issue.code === 'MissingHeader') {
    const names = ['PERFIL', 'PLATAFORMA', 'PROXY', 'PORTA', 'USUARIO', 'SENHA', 'STATUS', 'DATA DE VENCIMENTO']
    const name = names.find(value => issue.message === `Cabeçalho obrigatório ausente: ${value}.`)
    if (name) messages.MissingHeader = `Cabeçalho obrigatório ausente: ${name}.`
  }
  return `Linha ${issue.line}${issue.column ? `, coluna ${issue.column}` : ''}: ${messages[issue.code] ?? 'A API identificou um erro ou aviso nesta posição. Revise o CSV.'}`
}
