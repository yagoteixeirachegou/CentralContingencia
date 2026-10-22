import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { App } from '../../../app/App'
import type { CsvPreview, CsvReport } from './contracts'
let roles: string[]
let preview: CsvPreview
let status: number
let commitStatus: number
let commitDelay: Promise<void> | undefined
let networkFailure: boolean
let requests: { url: URL; options?: RequestInit }[]
const report: CsvReport = { importId: '00000000-0000-4000-8000-000000000099', replayed: false, total: 1, imported: 1, ignored: 0, invalid: 0, conflicting: 0, items: [{ line: 2, proxyId: '00000000-0000-4000-8000-000000000001', outcome: 'Imported' }] }
const json = (data: unknown, code = 200) => new Response(JSON.stringify(data), { status: code, headers: { 'Content-Type': 'application/json' } })
const file = () => new File(['synthetic-secret-password,synthetic-full-user'], 'synthetic.csv', { type: 'text/csv' })
beforeEach(() => {
  roles = ['Admin']; status = 200; commitStatus = 200; commitDelay = undefined; networkFailure = false; requests = []
  preview = { columns: [{ position: 1, field: 'DATA DE VENCIMENTO' }], headerIssues: [], summary: { total: 1, valid: 1, invalid: 0, duplicates: 0, empty: 0, conflicts: 0, warnings: 0 },
    page: 1, pageSize: 100, items: [{ line: 2, endLine: 2, hostMasked: '***', port: 8080, status: 'Active', expiresAt: '2099-12-31T00:00:00Z', regionPresent: true, valid: true, duplicate: false, existingConflict: false, errors: [], warnings: [] }],
    confirmationToken: 'synthetic-sensitive-proof', confirmationExpiresAt: new Date(Date.now() + 900_000).toISOString() }
  window.history.replaceState({}, '', '/proxies/import')
  vi.stubGlobal('fetch', vi.fn(async (input: string, options?: RequestInit) => {
    const url = new URL(input, 'https://localhost'); requests.push({ url, options })
    if (url.pathname.endsWith('/me')) return json({ id: 'synthetic-admin', email: 'admin@example.test', roles })
    if (url.pathname.endsWith('/csrf')) return json({ token: 'synthetic-csrf' })
    if (url.pathname.endsWith('/preview')) return status === 200 ? json({ ...preview, page: Number(url.searchParams.get('page')) }) : json({}, status)
    if (url.pathname.endsWith('/commit')) {
      if (commitDelay) await commitDelay
      if (networkFailure) throw new Error('Synthetic network failure')
      return commitStatus === 200 ? json(report) : json({ report: { ...report, importId: null, imported: 0, invalid: 1, items: [{ line: 2, proxyId: null, outcome: 'Invalid' }] } }, commitStatus)
    }
    return json({ total: 0, page: 1, pageSize: 20, items: [], evaluatedAt: new Date().toISOString() })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
const calls = (suffix: string) => requests.filter(request => request.url.pathname.endsWith(suffix))
async function page() { render(<App />); await screen.findByRole('heading', { name: 'Importar CSV' }) }
function select(chosen = file()) { fireEvent.change(screen.getByLabelText('Arquivo CSV'), { target: { files: [chosen] } }) }
async function analyze() { select(); fireEvent.click(screen.getByRole('button', { name: 'Gerar prévia' })); await screen.findByText('Resumo do arquivo completo') }
async function confirmation() { fireEvent.click(screen.getByRole('button', { name: 'Revisar confirmação' })); return screen.findByRole('dialog') }
test('ano começa vazio e não usa o ano atual automaticamente; data interpretada vem da API em UTC', async () => {
  await page(); expect(screen.getByLabelText('Ano de referência (opcional)')).toHaveValue(''); await analyze()
  expect(calls('/preview')[0].url.searchParams.has('referenceYear')).toBe(false)
  expect(screen.getByText(/31\/12\/2099.*00:00:00 UTC/)).toBeInTheDocument()
})
test('ano explícito acompanha prévia e confirmação; editar invalida a prova', async () => {
  await page(); await analyze()
  fireEvent.change(screen.getByLabelText('Ano de referência (opcional)'), { target: { value: '2096' } })
  expect(screen.queryByText('Resumo do arquivo completo')).not.toBeInTheDocument()
  preview.items[0].expiresAt = '2096-02-29T00:00:00Z'; preview.items[0].warnings = [{ line: 2, column: 1, code: 'ReferenceYearApplied', message: '' }]
  fireEvent.click(screen.getByRole('button', { name: 'Gerar prévia' })); await screen.findByText(/29\/02\/2096.*UTC/)
  expect(screen.getByText(/Ano de referência explícito aplicado/)).toBeInTheDocument()
  const modal = await confirmation(); expect(within(modal).getByText(/Ano de referência: 2096/)).toBeInTheDocument()
  fireEvent.click(within(modal).getByRole('button', { name: 'Confirmar importação' })); await screen.findByText('Relatório da importação')
  expect(calls('/preview').at(-1)?.url.searchParams.get('referenceYear')).toBe('2096')
  expect(calls('/commit')[0].url.searchParams.get('referenceYear')).toBe('2096')
})
test.each(['0', '10000', '-1', '2026.5', 'abc'])('ano inválido %s impede envio', async value => {
  await page(); select(); fireEvent.change(screen.getByLabelText('Ano de referência (opcional)'), { target: { value } })
  fireEvent.click(screen.getByRole('button', { name: 'Gerar prévia' })); expect(await screen.findByRole('alert')).toHaveTextContent('Informe um ano inteiro')
  expect(calls('/preview')).toHaveLength(0)
})

test('arquivo só é enviado mediante ação explícita, multipart e CSRF compartilhado', async () => {
  await page(); select(); expect(calls('/preview')).toHaveLength(0)
  fireEvent.click(screen.getByRole('button', { name: 'Gerar prévia' })); await screen.findByText('Resumo do arquivo completo')
  const request = calls('/preview')[0]; expect(request.options?.body).toBeInstanceOf(FormData)
  expect((request.options?.headers as Record<string, string>)['X-CSRF-TOKEN']).toBe('synthetic-csrf')
  expect((request.options?.headers as Record<string, string>)['Content-Type']).toBeUndefined()
  expect(request.options?.credentials).toBe('same-origin'); expect(request.url.searchParams.get('pageSize')).toBe('100')
  expect((request.options?.body as FormData).get('file')).toBeInstanceOf(File)
})
test.each(['empty', 'extension', 'large'])('rejeita arquivo %s sem requisição', async mode => {
  await page()
  select(mode === 'empty' ? new File([], 'empty.csv') : mode === 'extension' ? new File(['synthetic'], 'synthetic.xlsx') : new File([new Uint8Array(2 * 1024 * 1024 + 1)], 'large.csv'))
  expect(screen.getByRole('alert')).toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Gerar prévia' })).toBeDisabled(); expect(calls('/preview')).toHaveLength(0)
})
test('prévia mostra totais, linha original e credenciais nunca entram no DOM/storage', async () => {
  const local = vi.spyOn(Storage.prototype, 'setItem')
  await page(); await analyze()
  expect(screen.getByText(/Total: 1 · Válidos: 1/)).toBeInTheDocument(); expect(screen.getByText('Informada no CSV')).toBeInTheDocument()
  expect(document.body.textContent).not.toContain('synthetic-secret-password'); expect(document.body.textContent).not.toContain('synthetic-full-user'); expect(document.body.textContent).not.toContain('synthetic-sensitive-proof')
  expect(local).not.toHaveBeenCalled()
})
test('data inválida e cabeçalhos obrigatórios ausentes bloqueiam confirmação', async () => {
  preview.summary.invalid = 1; preview.summary.valid = 0; preview.headerIssues = [{ line: 1, column: null, code: 'MissingHeader', message: 'not rendered' }]
  preview.items[0].valid = false; preview.items[0].errors = [{ line: 2, column: 1, code: 'InvalidField', message: 'not rendered' }]
  await page(); await analyze(); expect(screen.getByText(/Cabeçalho obrigatório ausente/)).toBeInTheDocument(); expect(screen.getByText(/Data de vencimento inválida/)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Revisar confirmação' })).toBeDisabled()
})
test('região ausente é explícita, alteração invalida prova e segue query idêntica', async () => {
  preview.items[0].regionPresent = false; preview.items[0].valid = false; preview.items[0].errors = [{ line: 2, column: null, code: 'InvalidField', message: '' }]; preview.summary.invalid = 1; preview.summary.valid = 0
  await page(); await analyze(); expect(screen.getByRole('button', { name: 'Revisar confirmação' })).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Região complementar (opcional)'), { target: { value: 'BR / São Paulo' } }); expect(screen.queryByText('Resumo do arquivo completo')).not.toBeInTheDocument()
  preview.items[0].errors = []; preview.items[0].valid = true; preview.summary.invalid = 0; preview.summary.valid = 1
  fireEvent.click(screen.getByRole('button', { name: 'Gerar prévia' })); await screen.findByText('Complementar: BR / São Paulo')
  const modal = await confirmation(); fireEvent.click(within(modal).getByRole('button', { name: 'Confirmar importação' })); await screen.findByText('Relatório da importação')
  expect(calls('/preview').at(-1)?.url.searchParams.get('defaultRegion')).toBe('BR / São Paulo'); expect(calls('/commit')[0].url.searchParams.get('defaultRegion')).toBe('BR / São Paulo')
})
test('duplicatas e conflitos são apresentados e backend é autoridade final', async () => {
  preview.summary.duplicates = 1; preview.summary.invalid = 1; preview.summary.valid = 0; preview.summary.conflicts = 1
  preview.items[0].duplicate = true; preview.items[0].existingConflict = true; preview.items[0].errors = [{ line: 2, column: null, code: 'DuplicateRecord', message: '' }]
  await page(); await analyze(); expect(screen.getByText('Conflito existente')).toBeInTheDocument(); expect(screen.getByText(/Registro duplicado no arquivo/)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Revisar confirmação' })).toBeEnabled()
})
test('duplicata com erro adicional não é ignorável', async () => {
  preview.summary.duplicates = 1; preview.summary.invalid = 1; preview.summary.valid = 0
  preview.items[0].duplicate = true; preview.items[0].errors = [{ line: 2, column: null, code: 'DuplicateRecord', message: '' }, { line: 2, column: 1, code: 'InvalidField', message: '' }]
  await page(); await analyze(); expect(screen.getByRole('button', { name: 'Revisar confirmação' })).toBeDisabled()
})
test('paginação reenvia arquivo e exige revisão integral quando há inválidos ignoráveis', async () => {
  preview.summary = { ...preview.summary, total: 101, valid: 100, invalid: 1, duplicates: 1 }
  await page(); await analyze(); expect(screen.getByRole('button', { name: 'Revisar confirmação' })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Próxima página' })); await screen.findByText('Página 2 de 2')
  expect(screen.getByRole('button', { name: 'Revisar confirmação' })).toBeEnabled(); expect(calls('/preview')[1].options?.body).toBeInstanceOf(FormData)
})
test('confirmar requer diálogo explícito; cancelar não grava', async () => {
  await page(); await analyze(); const modal = await confirmation(); expect(calls('/commit')).toHaveLength(0)
  fireEvent.click(within(modal).getByRole('button', { name: 'Cancelar' })); expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); expect(calls('/commit')).toHaveLength(0)
})
test('confirmação envia prova e UUID e bloqueia envio duplo até concluir', async () => {
  let finish!: () => void; commitDelay = new Promise(resolve => { finish = resolve })
  await page(); await analyze(); const modal = await confirmation(); const button = within(modal).getByRole('button', { name: 'Confirmar importação' })
  fireEvent.click(button); fireEvent.click(button)
  await waitFor(() => expect(calls('/commit')).toHaveLength(1)); expect(within(modal).getByRole('button', { name: 'Importando…' })).toBeDisabled()
  const headers = calls('/commit')[0].options?.headers as Record<string, string>
  expect(headers['X-Import-Confirmation']).toBe('synthetic-sensitive-proof'); expect(headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
  finish(); await screen.findByText('Relatório da importação'); expect(screen.queryByText('Resumo do arquivo completo')).not.toBeInTheDocument()
})
test.each([400, 403, 413, 415, 429, 503])('falha HTTP %s é segura e clara', async code => {
  status = code; await page(); select(); fireEvent.click(screen.getByRole('button', { name: 'Gerar prévia' })); expect(await screen.findByRole('alert')).toBeInTheDocument(); expect(calls('/commit')).toHaveLength(0)
})
test.each([503, 'network'])('retry %s conserva a chave da operação', async failure => {
  commitStatus = failure === 503 ? 503 : 200; networkFailure = failure === 'network'
  await page(); await analyze(); let modal = await confirmation(); fireEvent.click(within(modal).getByRole('button', { name: 'Confirmar importação' })); await screen.findByRole('alert')
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  commitStatus = 200; networkFailure = false; modal = await confirmation(); fireEvent.click(within(modal).getByRole('button', { name: 'Confirmar importação' })); await screen.findByText('Relatório da importação')
  expect((calls('/commit')[0].options?.headers as Record<string, string>)['Idempotency-Key']).toBe((calls('/commit')[1].options?.headers as Record<string, string>)['Idempotency-Key'])
})
test('409 exige nova prévia e preserva UUID para recuperar replay', async () => {
  commitStatus = 409; await page(); await analyze(); let modal = await confirmation(); fireEvent.click(within(modal).getByRole('button', { name: 'Confirmar importação' })); await screen.findByRole('alert')
  expect(screen.queryByRole('button', { name: 'Revisar confirmação' })).not.toBeInTheDocument()
  commitStatus = 200; fireEvent.click(screen.getByRole('button', { name: 'Gerar prévia' })); await screen.findByText('Resumo do arquivo completo'); modal = await confirmation()
  fireEvent.click(within(modal).getByRole('button', { name: 'Confirmar importação' })); await screen.findByText('Relatório da importação')
  expect((calls('/commit')[0].options?.headers as Record<string, string>)['Idempotency-Key']).toBe((calls('/commit')[1].options?.headers as Record<string, string>)['Idempotency-Key'])
})
test('422 apresenta relatório final sem conteúdo sensível', async () => {
  commitStatus = 422; await page(); await analyze(); const modal = await confirmation(); fireEvent.click(within(modal).getByRole('button', { name: 'Confirmar importação' })); await screen.findByText('Linha 2: Inválido')
  expect(screen.getByRole('alert')).toHaveTextContent('Nenhum registro foi gravado')
})
test('prova expirada bloqueia confirmação', async () => {
  preview.confirmationExpiresAt = new Date(Date.now() - 1000).toISOString(); await page(); await analyze()
  expect(screen.getByRole('alert')).toHaveTextContent('A prova expirou'); expect(screen.getByRole('button', { name: 'Revisar confirmação' })).toBeDisabled()
})
test('ausência da prova protegida bloqueia confirmação', async () => {
  preview.confirmationToken = null; await page(); await analyze(); expect(screen.getByRole('button', { name: 'Revisar confirmação' })).toBeDisabled()
})
test('opção explícita invalida prova e acompanha prévia/commit; linha ignorada consta no relatório', async () => {
  await page(); await analyze(); const option = screen.getByLabelText(/Ignorar explicitamente linhas com host/)
  expect(option).not.toBeChecked(); fireEvent.click(option); expect(screen.queryByText('Resumo do arquivo completo')).not.toBeInTheDocument()
  preview.summary.valid = 0; preview.summary.invalid = 1; preview.summary.empty = 1
  preview.items[0].valid = false; preview.items[0].errors = [{ line: 2, column: null, code: 'EmptyRow', message: '' }]
  preview.items[0].warnings = [{ line: 2, column: null, code: 'NoConnectionRowSkipped', message: '' }]
  fireEvent.click(screen.getByRole('button', { name: 'Gerar prévia' })); await screen.findByText('Será ignorada: sem conexão')
  const modal = await confirmation(); expect(within(modal).getByText(/Você autorizou ignorar/)).toBeInTheDocument()
  fireEvent.click(within(modal).getByRole('button', { name: 'Confirmar importação' })); await screen.findByText('Relatório da importação')
  expect(calls('/preview').at(-1)?.url.searchParams.get('skipRowsWithoutConnection')).toBe('true')
  expect(calls('/commit')[0].url.searchParams.get('skipRowsWithoutConnection')).toBe('true')
})
test('ano ausente bloqueia confirmação e aviso da coluna duplicada vazia é claro', async () => {
  preview.headerIssues = [{ line: 1, column: 3, code: 'IgnoredEmptyDuplicateHeader', message: '' }]
  preview.summary.invalid = 1; preview.summary.valid = 0; preview.items[0].valid = false
  preview.items[0].errors = [{ line: 2, column: 1, code: 'ExpiryYearRequired', message: '' }]
  await page(); await analyze(); expect(screen.getByText(/Informe Ano de referência ou complete/)).toBeInTheDocument()
  expect(screen.getByText(/Coluna duplicada totalmente vazia ignorada/)).toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Revisar confirmação' })).toBeDisabled()
})
test('sessão expirada reutiliza tratamento compartilhado e descarta interface de importação', async () => {
  status = 401; await page(); select(); fireEvent.click(screen.getByRole('button', { name: 'Gerar prévia' }))
  await screen.findByText('Sessão expirada. Entre novamente.'); expect(screen.queryByRole('heading', { name: 'Importar CSV' })).not.toBeInTheDocument()
})
test('após sucesso voltar à listagem consulta os proxies atualizados', async () => {
  await page(); await analyze(); const modal = await confirmation(); fireEvent.click(within(modal).getByRole('button', { name: 'Confirmar importação' })); await screen.findByText('Relatório da importação')
  fireEvent.click(screen.getByRole('link', { name: 'Voltar para proxies' })); await screen.findByRole('heading', { name: 'Proxies' })
  await waitFor(() => expect(calls('/operational-grid').length).toBeGreaterThan(0))
})
test('troca de arquivo apaga prévia e prova', async () => {
  await page(); await analyze(); select(new File(['different'], 'other.csv')); expect(screen.queryByText('Resumo do arquivo completo')).not.toBeInTheDocument(); expect(calls('/commit')).toHaveLength(0)
})
test.each(['Manager', 'Operator'])('%s não acessa importação nem dispara requisições', async role => {
  roles = [role]; render(<App />); await screen.findByRole('heading', { name: 'Acesso negado' }); expect(calls('/preview')).toHaveLength(0); expect(calls('/commit')).toHaveLength(0)
})
test.each(['Admin', 'Manager', 'Operator'])('navegação Importar CSV só aparece para Admin: %s', async role => {
  roles = [role]; window.history.replaceState({}, '', '/proxies'); render(<App />); await screen.findByRole('heading', { name: 'Proxies' })
  expect(Boolean(screen.queryByRole('link', { name: 'Importar CSV' }))).toBe(role === 'Admin')
})
