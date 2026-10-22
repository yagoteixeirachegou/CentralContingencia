import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { App } from '../../app/App'
import type { IngestionStatus, UploadReport } from './api'

let roles: string[]
let status: IngestionStatus
let submitStatus: number
let requests: { url: URL; options?: RequestInit }[]
const report: UploadReport = { title: 'Concluído', message: 'Criados 2 de 2 perfil(is).', groupName: 'FB Teste | YAGO', environment: 'Test', failed: false,
  summary: { requested: 2, reserved: 2, created: 2, adsPowerFailures: 0, unfilledForLackOfProxy: 0, filesForReview: 0 },
  created: [{ profile: 'Perfil 1255', adsPowerId: 'k1abc12', serial: '55', confirmedInCentral: true, warning: null }, { profile: 'Perfil 1256', adsPowerId: 'k1abc13', serial: '56', confirmedInCentral: true, warning: null }],
  failures: [], unfilled: [], review: [], reservationsUndone: null }
const json = (data: unknown, code = 200) => new Response(JSON.stringify(data), { status: code, headers: { 'Content-Type': 'application/json' } })
const txt = (name: string, size = 20) => new File(['x'.repeat(size)], name, { type: 'text/plain' })
beforeEach(() => {
  roles = ['Manager']; submitStatus = 200; requests = []
  status = { configured: true, inProgress: false, availableProxies: 7, nextProfileNumber: 1255, nextProfileName: 'Perfil 1255', group: { id: '10906211', name: 'FB Teste | YAGO', mode: 'Test' }, maxFiles: 3, maxFileBytes: 2048 }
  window.history.replaceState({}, '', '/proxies/ingestion')
  vi.stubGlobal('fetch', vi.fn(async (input: string, options?: RequestInit) => {
    const url = new URL(input, 'https://localhost'); requests.push({ url, options })
    if (url.pathname.endsWith('/me')) return json({ id: 'synthetic-manager', email: 'manager@example.test', roles })
    if (url.pathname.endsWith('/csrf')) return json({ token: 'synthetic-csrf' })
    if (url.pathname.endsWith('/profile-ingestions/status')) return json(status)
    if (url.pathname.endsWith('/profile-ingestions/submit')) return submitStatus === 200 ? json(report) : json({ title: 'Não foi possível falar com o n8n. Confira se ele está aberto e se o fluxo está ativo.', status: submitStatus }, submitStatus)
    return json({ total: 0, page: 1, pageSize: 20, items: [], evaluatedAt: new Date().toISOString() })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
const submits = () => requests.filter(request => request.url.pathname.endsWith('/submit'))
async function page() { render(<App />); await screen.findByRole('heading', { name: /Novos perfis/ }) }
const choose = (...files: File[]) => fireEvent.change(screen.getByLabelText('Arquivos TXT'), { target: { files } })

test('operador não vê a tela de envio', async () => {
  roles = ['Operator']; render(<App />)
  expect(await screen.findByText('Somente Admin e Manager podem enviar perfis.', {}, { timeout: 10000 })).toBeTruthy()
  expect(requests.some(request => request.url.pathname.endsWith('/status'))).toBe(false)
})

test('mostra ambiente de teste, grupo, proxies livres e próximo perfil', async () => {
  await page()
  expect(await screen.findByText('TESTE')).toBeTruthy(); expect(screen.getByText('FB Teste | YAGO')).toBeTruthy()
  expect(screen.getByText('7')).toBeTruthy(); expect(screen.getByText('Perfil 1255')).toBeTruthy()
})

test('sem configuração do n8n o envio fica bloqueado', async () => {
  status = { ...status, configured: false }; await page()
  await screen.findByText(/ainda não foi configurado/)
  choose(txt('a.txt'))
  expect((await screen.findByRole('button', { name: 'Revisar e enviar' })).hasAttribute('disabled')).toBe(true)
})

test('rejeita arquivos que não são .txt, repetidos, vazios ou grandes demais', async () => {
  await page(); await screen.findByText('TESTE')
  choose(txt('a.csv')); expect(await screen.findByRole('alert')).toHaveProperty('textContent', 'a.csv: somente arquivos .txt.')
  choose(txt('vazio.txt', 0)); await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('arquivo vazio'))
  choose(txt('grande.txt', 4096)); await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('acima de 2 KB'))
  choose(txt('1.txt'), txt('2.txt'), txt('3.txt'), txt('4.txt')); await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('No máximo 3'))
  expect(screen.queryByRole('list', { name: 'Arquivos selecionados' })).toBeNull(); expect(submits()).toHaveLength(0)
})

test('confirma antes de enviar, envia multipart com CSRF e limpa os arquivos', async () => {
  await page(); await screen.findByText('TESTE')
  choose(txt('a.txt'), txt('b.txt'))
  expect(within(screen.getByRole('list', { name: 'Arquivos selecionados' })).getAllByRole('listitem')).toHaveLength(2)
  fireEvent.click(screen.getByRole('button', { name: 'Revisar e enviar' }))
  const dialog = await screen.findByRole('dialog'); expect(dialog.textContent).toContain('TESTE'); expect(dialog.textContent).toContain('Perfil 1255'); expect(submits()).toHaveLength(0)
  fireEvent.click(screen.getByRole('button', { name: 'Enviar agora' }))
  expect(await screen.findByText('Perfis criados')).toBeTruthy(); expect(screen.getByText('Perfil 1256')).toBeTruthy()
  const goal = screen.getByRole('progressbar', { name: 'Perfis criados' }); expect(goal.getAttribute('aria-valuenow')).toBe('2'); expect(goal.getAttribute('aria-valuemax')).toBe('2'); expect(screen.getByText('2 de 2 perfis criados')).toBeTruthy()
  const [call] = submits(); expect(submits()).toHaveLength(1)
  expect((call.options?.headers as Record<string, string>)['X-CSRF-TOKEN']).toBe('synthetic-csrf')
  const body = call.options?.body as FormData; expect(body.getAll('files').map(file => (file as File).name)).toEqual(['a.txt', 'b.txt'])
  await waitFor(() => expect(screen.queryByRole('list', { name: 'Arquivos selecionados' })).toBeNull())
})

test('falha do n8n mostra a mensagem da API, mantém os arquivos e não reenvia sozinho', async () => {
  submitStatus = 503; await page(); await screen.findByText('TESTE')
  choose(txt('a.txt')); fireEvent.click(screen.getByRole('button', { name: 'Revisar e enviar' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Enviar agora' }))
  expect((await screen.findByRole('alert')).textContent).toContain('Não foi possível falar com o n8n')
  expect(within(screen.getByRole('list', { name: 'Arquivos selecionados' })).getAllByRole('listitem')).toHaveLength(1); expect(submits()).toHaveLength(1)
})

test('avisa quando há mais arquivos do que proxies livres', async () => {
  status = { ...status, availableProxies: 1 }; await page(); await screen.findByText('TESTE')
  choose(txt('a.txt'), txt('b.txt'))
  expect(await screen.findByText(/1 ficarão sem perfil/)).toBeTruthy()
})

test('durante o envio mostra os arquivos em processamento, tempo decorrido e estimativa, sem contagem inventada', async () => {
  let finish: () => void = () => undefined; const gate = new Promise<void>(resolve => { finish = resolve })
  const original = globalThis.fetch
  vi.stubGlobal('fetch', vi.fn(async (input: string, options?: RequestInit) => { if (String(input).endsWith('/profile-ingestions/submit')) await gate; return original(input, options) }))
  await page(); await screen.findByText('TESTE'); choose(txt('a.txt'), txt('b.txt'))
  fireEvent.click(screen.getByRole('button', { name: 'Revisar e enviar' })); fireEvent.click(await screen.findByRole('button', { name: 'Enviar agora' }))
  expect(await screen.findByText(/Processando 2 arquivo[(]s[)]/)).toBeTruthy(); expect(screen.getByText(/Tempo decorrido 00:0[0-9] · estimativa de cerca de 00:22/)).toBeTruthy()
  expect(screen.getByRole('list', { name: 'Arquivos em processamento' })).toBeTruthy()
  finish(); expect(await screen.findByText('Perfis criados')).toBeTruthy()
})
