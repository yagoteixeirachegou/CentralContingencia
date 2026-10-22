import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { App } from '../../../app/App'
import { gridReturn } from './navigation'
import { listOperationalGrid } from './api'
import type { GridRow } from './contracts'
const profileId = '10000000-0000-4000-8000-000000000001'
const id = (number: number) => `00000000-0000-4000-8000-${String(number).padStart(12, '0')}`
const base: GridRow = { proxyId: id(1), host: 'grid01.synthetic.test', port: 8080, region: 'Brasil Sul', expiresAt: '2099-01-01T12:00:00Z', status: 'Active', availability: 'Available', profileId: null, profileName: null, platform1: null, platform2: null, allocationId: null, reservationExpiresAt: null }
let rows: GridRow[]; let roles: string[]; let status: number; let total: number; let writesStatus: number; let actionStatus: number
let calls: { url: URL; options?: RequestInit }[]
const json = (data: unknown, code = 200) => new Response(JSON.stringify(data), { status: code, headers: { 'Content-Type': 'application/json' } })
beforeEach(() => {
  roles = ['Admin']; status = 200; total = 2000; writesStatus = 201; actionStatus = 204; calls = []
  rows = [base, { ...base, proxyId: id(2), host: 'grid02.synthetic.test', profileId, profileName: 'Perfil 123', platform1: 'DARKPOST', platform2: 'TIKTOK', availability: 'Reserved', allocationId: id(10), reservationExpiresAt: '2099-01-01T12:00:00Z' },
    { ...base, proxyId: id(3), host: 'grid03.synthetic.test', profileId: id(21), profileName: 'Perfil vencido', platform1: 'ATIVO (LIVRE)', status: 'Suspended', availability: 'InUse', expiresAt: '2000-01-01T00:00:00Z', allocationId: id(11) },
    { ...base, proxyId: id(4), host: 'grid04.synthetic.test', availability: 'Unavailable', expiresAt: '2000-01-01T00:00:00Z' }]
  window.history.replaceState({}, '', '/proxies')
  vi.stubGlobal('fetch', vi.fn(async (input: string, options?: RequestInit) => {
    const url = new URL(input, 'https://localhost'); calls.push({ url, options })
    if (url.pathname.endsWith('/me')) return json({ id: id(99), email: 'synthetic@example.test', roles })
    if (url.pathname.endsWith('/csrf')) return json({ token: 'synthetic-grid-csrf' })
    if (url.pathname.endsWith('/reserve')) return writesStatus === 201 ? json({ id: id(12), proxyId: id(1), profileId, status: 'Reserved' }, 201) : new Response('', { status: writesStatus })
    if (url.pathname.endsWith('/operational-grid')) return status === 200 ? json({ items: rows.map(row => ({ ...row, password: 'synthetic-hidden-password', username: 'synthetic-hidden-username' })), page: Number(url.searchParams.get('page')), pageSize: Number(url.searchParams.get('pageSize')), total, evaluatedAt: '2026-10-12T12:00:00Z' }) : new Response('', { status })
    if (url.pathname === '/api/v1/profiles' && options?.method === 'POST') return json({ id: id(30), name: JSON.parse(String(options.body)).name, platform: '', status: 'Active', createdAt: '2026-10-12T00:00:00Z', updatedAt: '2026-10-12T00:00:00Z' }, 201)
    if (url.pathname === '/api/v1/profiles' && url.searchParams.has('name') && url.searchParams.get('name') !== 'Perfil 123') return json({ items: [], page: 1, pageSize: 1, total: 0 })
    if (url.pathname.endsWith('/confirm') || url.pathname.endsWith('/release')) return new Response(null, { status: actionStatus })
    if (options?.method === 'PATCH' && url.pathname.startsWith('/api/v1/profiles/')) return json({ id: profileId, name: 'Perfil 123', platform: '', status: 'Active', createdAt: '2026-10-12T00:00:00Z', updatedAt: '2026-10-12T00:00:00Z' })
    if (url.pathname === '/api/v1/profiles') return json({ items: [{ id: profileId, name: 'Perfil 123', platform: '', status: 'Active', createdAt: '2026-10-12T00:00:00Z', updatedAt: '2026-10-12T00:00:00Z' }], page: 1, pageSize: 20, total: 1 })
    if (url.pathname === `/api/v1/profiles/${profileId}`) return json({ id: profileId, name: 'Perfil 123', platform: '', platform1: 'DARKPOST', platform2: 'TIKTOK', status: 'Active', createdAt: '2026-10-12T00:00:00Z', updatedAt: '2026-10-12T00:00:00Z' })
    if (url.pathname.includes('/allocations')) return json({ items: [], page: 1, pageSize: 20, total: 0 })
    if (url.pathname.startsWith('/api/v1/proxies/')) return json({ ...base, id: id(1), usernameMasked: '***', available: true, createdAt: '2026-10-12T00:00:00Z', updatedAt: '2026-10-12T00:00:00Z' })
    return new Response('', { status: 404 })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
const reads = () => calls.filter(call => call.url.pathname.endsWith('/operational-grid'))
async function page() { render(<App />); await screen.findByRole('region', { name: 'Grid operacional' }, { timeout: 10000 }) }
test('sete colunas, uma linha por proxy, campos vazios e classificação não confundida com disponibilidade', async () => {
  await page(); const table = within(screen.getByRole('region', { name: 'Grid operacional' }))
  expect(table.getAllByRole('columnheader').map(header => header.textContent)).toEqual(['Perfil', 'Plataformas', 'Proxy', 'Região', 'Vencimento', 'Disponibilidade', 'Ações'])
  expect(table.getAllByRole('row')).toHaveLength(5)
  expect(within(table.getAllByRole('row')[1]).getAllByRole('cell').slice(0, 2).every(cell => cell.textContent === '')).toBe(true)
  expect(table.getByText('Perfil vencido')).toBeInTheDocument(); expect(table.getByText('Em uso')).toBeInTheDocument(); expect(table.getByText('ATIVO (LIVRE)')).toBeInTheDocument()
  expect(screen.queryByText('synthetic-hidden-password')).not.toBeInTheDocument(); expect(screen.queryByText('synthetic-hidden-username')).not.toBeInTheDocument()
  expect(calls.filter(c => c.url.pathname.includes('/profiles')).length).toBe(0)
})
test('debounce agrupa digitação e preserva filtros textuais simultâneos e dropdowns', async () => {
  await page(); fireEvent.change(screen.getByLabelText('Perfil'), { target: { value: 'Per' } }); fireEvent.change(screen.getByLabelText('Perfil'), { target: { value: 'Perfil' } }); fireEvent.change(screen.getByLabelText('Região'), { target: { value: 'sul' } })
  fireEvent.change(screen.getByLabelText('Plataforma'), { target: { value: 'TIKTOK' } }); fireEvent.change(screen.getByLabelText('Disponibilidade'), { target: { value: 'InUse' } })
  expect(reads().some(c => c.url.searchParams.has('profile'))).toBe(false)
  await waitFor(() => expect(reads().some(c => c.url.searchParams.get('profile') === 'Perfil' && c.url.searchParams.get('region') === 'sul' && c.url.searchParams.get('platform') === 'TIKTOK' && c.url.searchParams.get('availability') === 'InUse')).toBe(true))
  expect(reads().some(c => c.url.searchParams.get('profile') === 'Per')).toBe(false)
  expect(new URLSearchParams(window.location.search).get('profile')).toBe('Perfil')
})
test('pagina e ordena no servidor com total de 2000 sem estimar da página parcial', async () => {
  await page(); expect(screen.getByText(/2000 resultado/)).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Próxima página de proxies' }))
  await waitFor(() => expect(reads().at(-1)?.url.searchParams.get('page')).toBe('2'))
  fireEvent.change(screen.getByLabelText('Vencimento'), { target: { value: 'desc' } })
  await waitFor(() => expect(reads().at(-1)?.url.searchParams.get('direction')).toBe('desc')); expect(reads().at(-1)?.url.searchParams.get('page')).toBe('1')
  fireEvent.change(screen.getByLabelText('Por página'), { target: { value: '100' } }); await waitFor(() => expect(reads().at(-1)?.url.searchParams.get('pageSize')).toBe('100'))
})
test('limpar cancela busca ainda não enviada e volta aos defaults', async () => {
  await page(); fireEvent.change(screen.getByLabelText('Perfil'), { target: { value: 'pending' } }); fireEvent.click(screen.getByRole('button', { name: 'Limpar' }))
  await new Promise(resolve => setTimeout(resolve, 450)); expect(reads().some(c => c.url.searchParams.has('profile'))).toBe(false); expect(screen.getByLabelText('Perfil')).toHaveValue('')
})
test('URL inicial preserva filtros e retorno do perfil restaura o grid', async () => {
  window.history.replaceState({}, '', '/proxies?region=sul&platform=TIKTOK&page=2&direction=desc'); await page()
  expect(screen.getByLabelText('Região')).toHaveValue('sul'); expect(reads()[0].url.searchParams.get('page')).toBe('2')
  fireEvent.click(screen.getByRole('link', { name: 'Perfil 123' })); await screen.findByRole('heading', { name: 'Perfil 123' }); fireEvent.click(screen.getByRole('link', { name: 'Voltar ao grid' }))
  await screen.findByRole('heading', { name: /^Proxies$/ }); expect(window.location.search).toContain('region=sul'); expect(screen.getByLabelText('Vencimento')).toHaveValue('desc')
})
test.each(['Admin', 'Manager', 'Operator'])('permissões de %s e navegação única', async role => {
  roles = [role]; await page(); expect(within(screen.getByRole('navigation', { name: 'Navegação principal' })).getByRole('link', { name: 'Perfis' })).toHaveAttribute('href', '/profiles')
  expect(screen.queryByRole('link', { name: 'Administrar perfis' })).not.toBeInTheDocument(); expect(screen.queryByRole('link', { name: /Novos perfis/ })).not.toBeInTheDocument()
  expect(Boolean(screen.queryByRole('button', { name: 'Novo proxy' }))).toBe(role !== 'Operator')
  expect(Boolean(screen.queryByRole('link', { name: 'Importar CSV' }))).toBe(role === 'Admin')
  for (const [host, item] of [[base.host, 'Vincular perfil'], ['grid02.synthetic.test', 'Plataformas'], ['grid02.synthetic.test', 'Confirmar reserva'], ['grid03.synthetic.test', 'Liberar proxy']])
    expect(await hasItem(host, item)).toBe(role !== 'Operator')
})
async function pick(host: string, item: string) { fireEvent.keyDown(screen.getByRole('button', { name: 'Ações de ' + host }), { key: 'Enter' }); fireEvent.click(await screen.findByRole('menuitem', { name: item })) }
async function hasItem(host: string, item: string) {
  fireEvent.keyDown(screen.getByRole('button', { name: 'Ações de ' + host }), { key: 'Enter' }); await screen.findByRole('menuitem', { name: 'Detalhes' })
  const present = screen.queryByRole('menuitem', { name: item }) !== null; fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' })
  await waitFor(() => expect(screen.queryByRole('menu')).toBeNull()); return present
}
const writes = () => calls.filter(c => ['POST', 'PATCH'].includes(c.options?.method ?? ''))
const path = (c: { url: URL }) => c.url.pathname.replace('/api/v1', '')
async function openLink() { await page(); await pick(base.host, 'Vincular perfil'); return within(await screen.findByRole('dialog')) }
test('vincular cria perfil novo, reserva e confirma em sequência com CSRF e idempotência', async () => {
  const dialog = await openLink(); fireEvent.change(dialog.getByLabelText('Nome do perfil'), { target: { value: ' Perfil 9000 ' } }); fireEvent.change(dialog.getByLabelText('Plataforma 1'), { target: { value: 'INFOPRODUTO' } })
  fireEvent.click(dialog.getByRole('button', { name: 'Vincular' })); await screen.findByText('Perfil 9000 vinculado a grid01.synthetic.test:8080.')
  expect(writes().map(path)).toEqual(['/profiles', '/allocations/reserve', `/allocations/${id(12)}/confirm`])
  expect(JSON.parse(String(writes()[0].options?.body))).toEqual({ name: 'Perfil 9000', platform1: 'INFOPRODUTO' })
  expect(JSON.parse(String(writes()[1].options?.body))).toEqual({ profileId: id(30), proxyId: id(1) })
  expect(writes()[1].options?.headers).toMatchObject({ 'X-CSRF-TOKEN': 'synthetic-grid-csrf', 'Idempotency-Key': expect.stringMatching(/^[0-9a-f-]{36}$/) })
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
})
test('nome de perfil ativo existente bloqueia criação duplicada', async () => {
  const dialog = await openLink(); fireEvent.change(dialog.getByLabelText('Nome do perfil'), { target: { value: 'Perfil 123' } }); fireEvent.click(dialog.getByRole('button', { name: 'Vincular' }))
  expect(await dialog.findByRole('alert')).toHaveTextContent('Já existe um perfil ativo'); expect(writes()).toHaveLength(0)
})
test('vincular perfil existente não cria perfil', async () => {
  const dialog = await openLink(); fireEvent.click(dialog.getByRole('tab', { name: 'Perfil existente' })); fireEvent.click(await dialog.findByRole('radio', { name: 'Perfil 123' })); fireEvent.click(dialog.getByRole('button', { name: 'Vincular' }))
  await screen.findByText('Perfil 123 vinculado a grid01.synthetic.test:8080.'); expect(writes().map(path)).toEqual(['/allocations/reserve', `/allocations/${id(12)}/confirm`])
})
test('reserva recusada após criar perfil informa o estado e a nova tentativa não recria o perfil', async () => {
  writesStatus = 409; const dialog = await openLink(); fireEvent.change(dialog.getByLabelText('Nome do perfil'), { target: { value: 'Perfil 9001' } }); fireEvent.click(dialog.getByRole('button', { name: 'Vincular' }))
  expect(await dialog.findByRole('alert')).toHaveTextContent('Perfil criado, mas a proxy não foi vinculada'); expect(writes().map(path)).toEqual(['/profiles', '/allocations/reserve'])
  writesStatus = 201; fireEvent.click(dialog.getByRole('button', { name: 'Tentar novamente' })); await screen.findByText('Perfil 9001 vinculado a grid01.synthetic.test:8080.')
  expect(writes().map(path)).toEqual(['/profiles', '/allocations/reserve', '/allocations/reserve', `/allocations/${id(12)}/confirm`])
  expect(writes()[1].options?.headers).toMatchObject({ 'Idempotency-Key': (writes()[2].options?.headers as Record<string, string>)['Idempotency-Key'] })
})
test('confirmar reserva pela linha envia uma única requisição', async () => {
  await page(); await pick('grid02.synthetic.test', 'Confirmar reserva')
  await screen.findByText('Reserva de grid02.synthetic.test:8080 confirmada para Perfil 123.'); expect(writes().map(path)).toEqual([`/allocations/${id(10)}/confirm`])
})
test('liberar exige confirmação humana e erro não gera nova tentativa', async () => {
  await page(); await pick('grid03.synthetic.test', 'Liberar proxy'); const dialog = within(await screen.findByRole('dialog'))
  expect(writes()).toHaveLength(0); actionStatus = 409; fireEvent.click(dialog.getByRole('button', { name: 'Confirmar liberação' }))
  expect(await dialog.findByRole('alert')).toHaveTextContent('Conflito operacional'); expect(writes().map(path)).toEqual([`/allocations/${id(11)}/release`])
  actionStatus = 204; fireEvent.click(dialog.getByRole('button', { name: 'Confirmar liberação' })); await screen.findByText('grid03.synthetic.test:8080 liberada de Perfil vencido.')
})
test('plataformas envia somente slots alterados e null para limpar', async () => {
  await page(); await pick('grid02.synthetic.test', 'Plataformas'); const dialog = within(await screen.findByRole('dialog'))
  expect(dialog.getByRole('button', { name: 'Salvar plataformas' })).toBeDisabled()
  fireEvent.change(dialog.getByLabelText('Plataforma 2'), { target: { value: '' } }); fireEvent.click(dialog.getByRole('button', { name: 'Salvar plataformas' }))
  await screen.findByText('Plataformas de Perfil 123 atualizadas.'); expect(writes().map(path)).toEqual([`/profiles/${profileId}`])
  expect(writes()[0].options?.method).toBe('PATCH'); expect(JSON.parse(String(writes()[0].options?.body))).toEqual({ platform2: null })
})
test('vazio e falha não geram linhas fictícias ou indicadores', async () => {
  rows = []; total = 0; render(<App />); await screen.findByRole('heading', { name: 'Nenhum proxy encontrado' }); status = 503; fireEvent.click(screen.getByRole('button', { name: 'Atualizar' })); await screen.findByRole('alert'); expect(screen.queryByRole('region', { name: 'Grid operacional' })).not.toBeInTheDocument()
})
test('dados incompatíveis retornam erro sanitizado e campos adicionais são removidos', async () => {
  const value = await listOperationalGrid({ page: 1, pageSize: 20, sort: 'expiresAt', direction: 'asc' }); expect(value.items[0]).not.toHaveProperty('password'); expect(value.items[0]).not.toHaveProperty('username')
  vi.stubGlobal('fetch', vi.fn(async () => json({ password: 'synthetic-sensitive-error' }))); await expect(listOperationalGrid({ page: 1, pageSize: 20, sort: 'expiresAt', direction: 'asc' })).rejects.toMatchObject({ status: 503, message: expect.not.stringContaining('synthetic-sensitive-error') })
})
test('retorno não permite URL externa', () => {
  expect(gridReturn({ returnTo: 'https://example.test/proxies' })).toBe('/proxies'); expect(gridReturn({ returnTo: '/proxies?region=BR' })).toBe('/proxies?region=BR'); expect(gridReturn({ returnTo: '//example.test/proxies' })).toBe('/proxies')
})
test('menu lateral lista as planilhas e o título usa o nome da aba', async () => {
  await page(); const nav = within(screen.getByRole('navigation', { name: 'Navegação principal' }))
  expect(nav.getByRole('button', { name: /^HTTP - FACEBOOK$/ })).toHaveAttribute('aria-expanded', 'true')
  expect(nav.getByRole('link', { name: 'Proxies' })).toHaveAttribute('href', '/proxies'); expect(nav.getByRole('link', { name: 'Perfis' })).toHaveAttribute('href', '/profiles')
  expect(nav.getByRole('link', { name: 'Proxies' })).toHaveAttribute('aria-current', 'page')
  expect(screen.getByRole('heading', { name: /^Proxies$/ })).toBeInTheDocument()
  fireEvent.click(nav.getByRole('button', { name: /^HTTP - FACEBOOK$/ })); expect(nav.getByRole('button', { name: /^HTTP - FACEBOOK$/ })).toHaveAttribute('aria-expanded', 'false')
})
test('abas ainda não migradas aparecem desativadas como Em breve, sem link', async () => {
  await page(); const nav = screen.getByRole('navigation', { name: 'Navegação principal' })
  for (const label of ['HTTP - TIKTOK', 'HTTP - GOOGLE ADS', 'NUMEROS - OPERAÇAO', 'NUMEROS - FACEBOOK']) {
    expect(within(nav).getByText(label)).toBeInTheDocument(); expect(within(nav).queryByRole('link', { name: label })).not.toBeInTheDocument()
  }
  expect(within(nav).getByRole('link', { name: 'Aquecimento' })).toHaveAttribute('href', '/warmup')
  expect(within(nav).getAllByText('Em breve')).toHaveLength(4)
})
