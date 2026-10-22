import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { App } from '../../app/App'
import type { ProxyItem } from './contracts'
const seed: ProxyItem = { id: '00000000-0000-4000-8000-000000000001', host: 'synthetic.example.test', port: 8080, usernameMasked: '***', region: 'BR', status: 'Active',
  expiresAt: '2099-01-01T12:00:56.789Z', createdAt: '2026-10-11T00:00:00Z', updatedAt: '2026-10-11T00:00:00Z', available: false }
let row: ProxyItem
let roles: string[]
let session: boolean
let listStatus: number
let writeStatus: number
let empty: boolean
let delayed: boolean
let detailStatus: number
let requests: { url: URL; options?: RequestInit }[]
const json = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status, headers: { 'Content-Type': 'application/json' } })
beforeEach(() => {
  row = { ...seed }; roles = ['Admin']; session = true; listStatus = 200; writeStatus = 200; detailStatus = 200; empty = false; delayed = false; requests = []
  window.history.replaceState({}, '', '/proxies')
  vi.stubGlobal('fetch', vi.fn(async (input: string, options?: RequestInit) => {
    const url = new URL(input, 'https://localhost'); requests.push({ url, options })
    if (url.pathname.endsWith('/csrf')) return json({ token: 'synthetic-csrf' })
    if (url.pathname.endsWith('/me')) return session ? json({ id: 'synthetic-admin', email: 'admin@example.test', roles }) : new Response('', { status: 401 })
    if (url.pathname.endsWith('/logout')) { session = false; return new Response(null, { status: 204 }) }
    if (url.pathname.endsWith('/login')) { session = true; return new Response(null, { status: 204 }) }
    if (options?.method === 'PATCH' || options?.method === 'POST') return writeStatus === 200 ? json(row, options.method === 'POST' ? 201 : 200) : new Response('', { status: writeStatus })
    if (url.pathname.endsWith(row.id)) return detailStatus === 200 ? json(row) : new Response('', { status: detailStatus })
    if (delayed) await new Promise(resolve => setTimeout(resolve, 50))
    if (listStatus !== 200) return new Response('', { status: listStatus })
    if (url.pathname.endsWith('/operational-grid')) return json({ items: empty ? [] : [{ proxyId: row.id, host: row.host, port: row.port, region: row.region, status: row.status, expiresAt: row.expiresAt, availability: 'Unavailable', profileId: null, profileName: null, platform1: null, platform2: null, allocationId: null, reservationExpiresAt: null }], page: Number(url.searchParams.get('page')), pageSize: Number(url.searchParams.get('pageSize')), total: empty ? 0 : 81, evaluatedAt: new Date().toISOString() })
    if (url.searchParams.get('pageSize') === '1') return json({ items: [], page: 1, pageSize: 1, total: url.pathname.endsWith('/available') ? 43 : url.searchParams.has('expiresBefore') ? 19 : 284 })
    return json({ items: empty ? [] : [row], page: Number(url.searchParams.get('page')), pageSize: Number(url.searchParams.get('pageSize')), total: empty ? 0 : 81 })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals() })
async function page() { render(<App />); await screen.findByText(seed.host, {}, { timeout: 3000 }) }
async function pick(host: string, item: string) { fireEvent.keyDown(screen.getByRole('button', { name: 'Ações de ' + host }), { key: 'Enter' }); fireEvent.click(await screen.findByRole('menuitem', { name: item })) }
async function hasItem(host: string, item: string) {
  fireEvent.keyDown(screen.getByRole('button', { name: 'Ações de ' + host }), { key: 'Enter' }); await screen.findByRole('menuitem', { name: 'Detalhes' })
  const present = screen.queryByRole('menuitem', { name: item }) !== null; fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' })
  await waitFor(() => expect(screen.queryByRole('menu')).toBeNull()); return present
}
async function openEdit() { await page(); await pick(seed.host, 'Editar'); await screen.findByLabelText('Novo usuário (opcional)'); return screen.getByRole('dialog') }
function change(label: string, value: string) { const modal = screen.queryByRole('dialog'); fireEvent.change((modal ? within(modal) : screen).getByLabelText(label), { target: { value } }) }
function fillCreate() {
  change('Host', 'new.synthetic.test'); change('Porta', '3128'); change('Região', 'BR'); change('Vencimento', '2099-01-01T12:00'); change('Usuário', 'synthetic-user'); change('Senha do proxy', 'Synthetic!123')
}
const writes = () => requests.filter(request => ['POST', 'PATCH'].includes(request.options?.method ?? ''))
test('total paginado vem da API e Active não implica disponibilidade', async () => {
  await page()
  expect(await screen.findByText(/81 resultado/)).toBeInTheDocument()
  expect(within(screen.getByRole('region', { name: 'Grid operacional' })).getByText('Indisponível')).toBeInTheDocument(); expect(screen.queryByText(/Usuário \*\*\*/)).not.toBeInTheDocument()
  expect(screen.queryByText('synthetic-user')).not.toBeInTheDocument()
})
test.each(['Admin', 'Manager'])('%s tem ações de cadastro e edição', async role => {
  roles = [role]; await page(); expect(screen.getByRole('button', { name: 'Novo proxy' })).toBeInTheDocument(); expect(await hasItem(seed.host, 'Editar')).toBe(true)
})
test('Operator consulta sem controles de escrita', async () => {
  roles = ['Operator']; await page(); expect(screen.getByText(/Modo de consulta/)).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Novo proxy' })).not.toBeInTheDocument(); expect(await hasItem(seed.host, 'Editar')).toBe(false)
})
test('papel desconhecido não dispara consultas e API 403 é acesso negado', async () => {
  roles = ['Unknown']; render(<App />); await screen.findByRole('heading', { name: 'Acesso negado' })
  expect(requests.some(request => request.url.pathname.includes('/proxies'))).toBe(false)
})
test('API 403 prevalece sobre permissão visual', async () => {
  listStatus = 403; render(<App />); await screen.findByRole('heading', { name: 'Acesso negado' })
})
test('filtros parciais, disponibilidade, ordenação e paginação são enviados ao grid', async () => {
  await page(); change('Perfil', 'parcial'); change('Região', 'BR'); change('Disponibilidade', 'Available')
  await waitFor(() => expect(requests.some(request => request.url.pathname.endsWith('/operational-grid') && request.url.searchParams.get('profile') === 'parcial' && request.url.searchParams.get('region') === 'BR' && request.url.searchParams.get('availability') === 'Available')).toBe(true))
  await waitFor(() => expect(screen.getByRole('button', { name: 'Próxima página de proxies' })).not.toBeDisabled())
  fireEvent.click(screen.getByRole('button', { name: 'Próxima página de proxies' }))
  await waitFor(() => expect(requests.some(request => request.url.searchParams.get('page') === '2')).toBe(true))
  change('Vencimento', 'desc')
  await waitFor(() => expect(requests.some(request => request.url.searchParams.get('sort') === 'expiresAt' && request.url.searchParams.get('direction') === 'desc' && request.url.searchParams.get('page') === '1')).toBe(true))
})
test('cadastro usa CSRF existente e consome resposta 201; não usa storage', async () => {
  const local = vi.spyOn(Storage.prototype, 'setItem')
  await page(); fireEvent.click(screen.getByRole('button', { name: 'Novo proxy' })); await screen.findByRole('dialog'); fillCreate()
  fireEvent.click(screen.getByRole('button', { name: 'Cadastrar proxy' }))
  await screen.findByText('Proxy cadastrado com sucesso.')
  const request = writes().at(-1)!
  expect(request.url.pathname).toBe('/api/v1/proxies'); expect(request.options?.headers).toMatchObject({ 'X-CSRF-TOKEN': 'synthetic-csrf' })
  expect(JSON.parse(String(request.options?.body))).toMatchObject({ host: 'new.synthetic.test', port: 3128, status: 'Disabled' })
  expect(local).not.toHaveBeenCalled(); local.mockRestore()
})
test('PATCH envia apenas alteração e preserva credenciais e vencimento passado', async () => {
  row.expiresAt = '2000-01-01T12:34:56.789Z'
  const modal = await openEdit()
  expect(within(modal).getByLabelText('Novo usuário (opcional)')).toHaveValue(''); expect(within(modal).getByLabelText('Nova senha (opcional)')).toHaveValue('')
  change('Status operacional', 'Suspended'); fireEvent.click(screen.getByRole('button', { name: 'Salvar alterações' }))
  await screen.findByText('Alterações salvas com sucesso.')
  expect(writes().at(-1)?.options?.method).toBe('PATCH'); expect(JSON.parse(String(writes().at(-1)?.options?.body))).toEqual({ status: 'Suspended' })
  expect(writes().at(-1)?.options?.headers).toMatchObject({ 'X-CSRF-TOKEN': 'synthetic-csrf' })
})
test('formulário vazio e novo vencimento passado não geram escrita', async () => {
  await page(); fireEvent.click(screen.getByRole('button', { name: 'Novo proxy' })); await screen.findByRole('dialog')
  fireEvent.click(screen.getByRole('button', { name: 'Cadastrar proxy' })); await screen.findByText(/Informe DNS/)
  fillCreate(); change('Vencimento', '2000-01-01T12:00'); fireEvent.click(screen.getByRole('button', { name: 'Cadastrar proxy' }))
  await screen.findByText('O novo vencimento deve estar no futuro.'); expect(writes()).toHaveLength(0)
})
test('PATCH sem alterações não é enviado', async () => {
  await openEdit(); fireEvent.click(screen.getByRole('button', { name: 'Salvar alterações' }))
  await screen.findByText('Altere pelo menos um campo para salvar.'); expect(writes()).toHaveLength(0)
})
test.each([400, 403, 409, 429, 503])('falha %s não repete gravação e limpa credenciais digitadas', async status => {
  writeStatus = status; await page(); fireEvent.click(screen.getByRole('button', { name: 'Novo proxy' })); await screen.findByRole('dialog'); fillCreate()
  fireEvent.click(screen.getByRole('button', { name: 'Cadastrar proxy' }))
  await screen.findByRole('alert'); expect(screen.getByLabelText('Senha do proxy')).toHaveValue(''); expect(screen.getByLabelText('Usuário')).toHaveValue('')
  expect(writes()).toHaveLength(1)
})
test('loading, vazio e recuperação de erro são explícitos', async () => {
  delayed = true; empty = true; render(<App />)
  await screen.findByText('Carregando proxies…'); await screen.findByRole('heading', { name: 'Nenhum proxy encontrado' })
  listStatus = 503; fireEvent.click(screen.getByRole('button', { name: 'Atualizar' }))
  await screen.findByRole('alert'); listStatus = 200; empty = false; fireEvent.click(screen.getByRole('button', { name: 'Tentar novamente' }))
  await screen.findByText(seed.host)
})
test('401 redireciona com aviso de sessão expirada', async () => {
  listStatus = 401; render(<App />)
  await screen.findByRole('heading', { name: 'Acesso interno' }); expect(screen.getByText('Sessão expirada. Entre novamente.')).toBeInTheDocument()
})
test('logout e expiração descartam a página com dados de proxies', async () => {
  await page(); act(() => window.dispatchEvent(new Event('session-expired')))
  await screen.findByRole('heading', { name: 'Acesso interno' }); expect(screen.queryByText(seed.host)).not.toBeInTheDocument()
})
test('troca explícita de credenciais envia só os campos preenchidos no PATCH', async () => {
  await openEdit(); change('Novo usuário (opcional)', 'replacement-user'); change('Nova senha (opcional)', 'Synthetic!456')
  fireEvent.click(screen.getByRole('button', { name: 'Salvar alterações' })); await screen.findByText('Alterações salvas com sucesso.')
  expect(JSON.parse(String(writes().at(-1)?.options?.body))).toEqual({ username: 'replacement-user', password: 'Synthetic!456' })
  expect(screen.queryByDisplayValue('Synthetic!456')).not.toBeInTheDocument()
})
test('detalhe 404 impede edição e oferece recuperação', async () => {
  detailStatus = 404; await page(); await pick(seed.host, 'Editar')
  await screen.findByText(/não foi encontrado/); expect(screen.queryByRole('button', { name: 'Salvar alterações' })).not.toBeInTheDocument()
  detailStatus = 200; fireEvent.click(screen.getByRole('button', { name: 'Tentar novamente' })); await screen.findByLabelText('Novo usuário (opcional)')
})
test('falha do grid não inventa totais zero', async () => {
  listStatus = 503; render(<App />); await screen.findByRole('alert'); expect(screen.queryByText(/0 resultado/)).not.toBeInTheDocument()
})
test('logout seguido de novo login busca dados novos em vez de reaproveitar cache', async () => {
  await page(); fireEvent.click(screen.getByRole('button', { name: 'Sair' })); await screen.findByRole('heading', { name: 'Acesso interno' })
  row.host = 'new-session.example.test'; change('E-mail', 'admin@example.test'); change('Senha', 'Synthetic!123')
  fireEvent.click(screen.getByRole('button', { name: 'Entrar' })); await screen.findByRole('heading', { name: 'Início' })
  fireEvent.click(screen.getByRole('link', { name: /^Proxies$/ })); await screen.findByText(row.host)
  expect(screen.queryByText(seed.host)).not.toBeInTheDocument()
})
test('tamanho de página respeita 100 e volta à primeira página', async () => {
  await page()
  change('Por página', '100')
  await waitFor(() => expect(requests.some(request => request.url.searchParams.get('pageSize') === '100' && request.url.searchParams.get('page') === '1')).toBe(true))
})
