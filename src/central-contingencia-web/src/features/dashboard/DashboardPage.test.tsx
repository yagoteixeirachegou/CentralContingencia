import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { App } from '../../app/App'
const json = (data: unknown, code = 200) => new Response(JSON.stringify(data), { status: code, headers: { 'Content-Type': 'application/json' } })
const profile = (profileId: string, name: string, groupId: string, groupName: string, tags: string[], createdAt: string | null, serialNumber: string) => ({ profileId, name, groupId, groupName, tags, createdAt, serialNumber })
const dashboard = { generatedAt: '2026-10-12T15:00:00Z', total: 6, fromCache: false,
  groups: [{ groupId: '1', groupName: '03 ESTEIRA DE PERFIL', total: 2, oldestCreatedAt: '2024-06-19T00:00:00Z', newestCreatedAt: '2025-01-10T00:00:00Z' },
    { groupId: '2', groupName: 'SPARK ADS TTK', total: 2, oldestCreatedAt: '2025-06-06T00:00:00Z', newestCreatedAt: '2026-05-01T00:00:00Z' },
    { groupId: '3', groupName: 'FB | FLAG', total: 2, oldestCreatedAt: '2026-09-01T00:00:00Z', newestCreatedAt: '2026-09-01T00:00:00Z' },
    { groupId: '4', groupName: 'DESCARTAR E SALVAR PROXY', total: 0, oldestCreatedAt: null, newestCreatedAt: null }],
  tags: [{ name: 'BACKUP', total: 3 }, { name: 'ESTEIRA DE PERFIL', total: 2 }, { name: 'FLAG', total: 1 }, { name: 'TTX', total: 1 }],
  profiles: [profile('p1', 'Perfil 10', '1', '03 ESTEIRA DE PERFIL', ['ESTEIRA DE PERFIL', 'BACKUP'], '2024-06-19T00:00:00Z', '10'), profile('p2', 'Perfil 2', '1', '03 ESTEIRA DE PERFIL', ['ESTEIRA DE PERFIL'], '2025-01-10T00:00:00Z', '2'),
    profile('p3', 'Spark 1', '2', 'SPARK ADS TTK', ['TTX'], '2026-05-01T00:00:00Z', '3'), profile('p4', 'FB Um', '3', 'FB | FLAG', ['FLAG', 'BACKUP'], '2026-09-01T00:00:00Z', '4'),
    profile('p5', 'Perfil 33', '3', 'FB | FLAG', [], null, '5'), profile('p6', 'Perfil 1', '2', 'SPARK ADS TTK', ['BACKUP'], '2025-06-06T00:00:00Z', '1')] }
let roles: string[]; let reads: number; let refreshes: number; let readResponse: () => Response; let refreshResponse: () => Response
beforeEach(() => {
  roles = ['Admin']; reads = 0; refreshes = 0; readResponse = () => json(dashboard); refreshResponse = () => json({ ...dashboard, total: 7, fromCache: false })
  window.history.replaceState({}, '', '/dashboard')
  vi.stubGlobal('fetch', vi.fn(async (input: string, options?: RequestInit) => {
    const url = new URL(input, 'https://localhost')
    if (url.pathname.endsWith('/me')) return json({ id: 'u1', email: 'synthetic@example.test', roles })
    if (url.pathname.endsWith('/csrf')) return json({ token: 'synthetic-csrf' })
    if (url.pathname.endsWith('/dashboard/refresh')) { refreshes++; expect(options?.method).toBe('POST'); return refreshResponse() }
    if (url.pathname.endsWith('/adspower/dashboard')) { reads++; return readResponse() }
    return new Response('', { status: 404 })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals() })
const open = async () => { render(<App />); await screen.findByRole('heading', { name: 'Dashboard' }); await screen.findByLabelText('03 ESTEIRA DE PERFIL: 2 perfis') }
const rows = () => within(screen.getByRole('table')).getAllByRole('row').slice(1).map(row => within(row).getAllByRole('cell')[1].textContent)
const summary = () => screen.getByRole('status', { name: '' }).textContent

test('mostra totais, blocos de grupos, tags e todos os perfis', async () => {
  await open()
  expect(screen.getByText('Perfis', { selector: 'p' }).nextSibling).toHaveTextContent('6'); expect(screen.getByText('Grupos', { selector: 'p' }).nextSibling).toHaveTextContent('4'); expect(screen.getByText('Tags', { selector: 'p' }).nextSibling).toHaveTextContent('4')
  expect(screen.getAllByRole('button').filter(button => /: \d+ perf(il|is)$/.test(button.getAttribute('aria-label') ?? '')).length).toBe(8) // 4 grupos + 4 tags
  expect(screen.getByLabelText('DESCARTAR E SALVAR PROXY: 0 perfis')).toBeInTheDocument(); expect(summary()).toBe('6 de 6 perfis')
  expect(rows()).toEqual(['Perfil 1', 'Perfil 2', 'Perfil 10', 'Perfil 33', 'Spark 1', 'FB Um'].sort((a, b) => a.localeCompare(b, 'pt-BR', { numeric: true })))
  expect(screen.getByText(/mais antigo há 2 anos e 3 meses/)).toBeInTheDocument(); expect(screen.getByText(/Credenciais, senhas e cookies dos perfis não são guardados nem exibidos/)).toBeInTheDocument()
})
test('clicar em um grupo filtra os perfis e clicar de novo remove o filtro; vários grupos valem como "qualquer um"', async () => {
  await open(); const tile = screen.getByLabelText('SPARK ADS TTK: 2 perfis')
  fireEvent.click(tile); expect(tile).toHaveAttribute('aria-pressed', 'true'); expect(rows()).toEqual(['Perfil 1', 'Spark 1']); expect(summary()).toBe('2 de 6 perfis')
  fireEvent.click(screen.getByLabelText('FB | FLAG: 2 perfis')); expect(rows()).toEqual(['FB Um', 'Perfil 1', 'Perfil 33', 'Spark 1'])
  fireEvent.click(tile); fireEvent.click(screen.getByLabelText('FB | FLAG: 2 perfis')); expect(rows()).toHaveLength(6)
})
test('várias tags exigem todas, e clicar em uma tag da tabela também filtra', async () => {
  await open(); fireEvent.click(screen.getByLabelText('BACKUP: 3 perfis')); expect(rows()).toEqual(['FB Um', 'Perfil 1', 'Perfil 10'])
  fireEvent.click(screen.getByLabelText('FLAG: 1 perfil')); expect(rows()).toEqual(['FB Um'])
  fireEvent.click(screen.getByRole('button', { name: 'Limpar filtros (2)' })); expect(rows()).toHaveLength(6)
  fireEvent.click(within(screen.getByRole('table')).getAllByRole('button', { name: 'ESTEIRA DE PERFIL' })[0]); expect(screen.getByLabelText('ESTEIRA DE PERFIL: 2 perfis')).toHaveAttribute('aria-pressed', 'true'); expect(rows()).toEqual(['Perfil 2', 'Perfil 10'])
})
test('buscar pelo nome, combinar filtros e mostrar estado vazio com saída', async () => {
  await open(); fireEvent.change(screen.getByLabelText('Buscar perfil pelo nome'), { target: { value: 'perfil' } }); expect(rows()).toEqual(['Perfil 1', 'Perfil 2', 'Perfil 10', 'Perfil 33'])
  fireEvent.click(screen.getByLabelText('SPARK ADS TTK: 2 perfis')); expect(rows()).toEqual(['Perfil 1'])
  fireEvent.change(screen.getByLabelText('Buscar perfil pelo nome'), { target: { value: 'zzz' } }); expect(screen.getByRole('heading', { name: 'Nenhum perfil com esses filtros' })).toBeInTheDocument(); expect(summary()).toBe('0 de 6 perfis')
  fireEvent.click(screen.getByRole('button', { name: 'Limpar filtros (2)' })); expect(rows()).toHaveLength(6); expect(screen.getByLabelText('Buscar perfil pelo nome')).toHaveValue('')
})
test('ordena os perfis pelos cabeçalhos, inclusive por data de criação com os sem data no fim', async () => {
  await open(); const table = () => within(screen.getByRole('table'))
  fireEvent.click(table().getByRole('button', { name: 'Perfil' })); expect(rows()[0]).toBe('Spark 1'); expect(table().getByRole('columnheader', { name: 'Perfil' })).toHaveAttribute('aria-sort', 'descending')
  fireEvent.click(table().getByRole('button', { name: 'Criado em' })); expect(rows()).toEqual(['Perfil 10', 'Perfil 2', 'Perfil 1', 'Spark 1', 'FB Um', 'Perfil 33'])
  fireEvent.click(table().getByRole('button', { name: 'Criado em' })); expect(rows()).toEqual(['FB Um', 'Spark 1', 'Perfil 1', 'Perfil 2', 'Perfil 10', 'Perfil 33'])
  fireEvent.click(table().getByRole('button', { name: 'Grupo' })); expect(rows()[0]).toBe('Perfil 2')
})
test('ordena os grupos por nome, quantidade e antiguidade', async () => {
  await open(); const order = () => screen.getAllByRole('button').map(button => button.getAttribute('aria-label') ?? '').filter(label => /perf(il|is)$/.test(label)).slice(0, 4).map(label => label.split(':')[0])
  const select = screen.getByLabelText('Ordenar por')
  fireEvent.change(select, { target: { value: 'name-asc' } }); expect(order()).toEqual(['03 ESTEIRA DE PERFIL', 'DESCARTAR E SALVAR PROXY', 'FB | FLAG', 'SPARK ADS TTK'])
  fireEvent.change(select, { target: { value: 'name-desc' } }); expect(order()).toEqual(['SPARK ADS TTK', 'FB | FLAG', 'DESCARTAR E SALVAR PROXY', '03 ESTEIRA DE PERFIL'])
  fireEvent.change(select, { target: { value: 'count-asc' } }); expect(order()[0]).toBe('DESCARTAR E SALVAR PROXY')
  fireEvent.change(select, { target: { value: 'oldest' } }); expect(order()).toEqual(['03 ESTEIRA DE PERFIL', 'SPARK ADS TTK', 'FB | FLAG', 'DESCARTAR E SALVAR PROXY'])
  fireEvent.change(select, { target: { value: 'newest' } }); expect(order()).toEqual(['FB | FLAG', 'SPARK ADS TTK', '03 ESTEIRA DE PERFIL', 'DESCARTAR E SALVAR PROXY'])
})
test('mostra 50 por vez com botão para ver mais', async () => {
  const many = Array.from({ length: 120 }, (_, i) => profile('x' + i, `Lote ${i + 1}`, '1', '03 ESTEIRA DE PERFIL', [], null, String(i)))
  readResponse = () => json({ ...dashboard, total: 120, profiles: many }); await open()
  expect(rows()).toHaveLength(50); fireEvent.click(screen.getByRole('button', { name: 'Mostrar mais 50' })); expect(rows()).toHaveLength(100)
  fireEvent.click(screen.getByRole('button', { name: 'Mostrar mais 20' })); expect(rows()).toHaveLength(120); expect(screen.queryByRole('button', { name: /Mostrar mais/ })).not.toBeInTheDocument()
})
test.each([['Admin', true], ['Manager', true], ['Operator', false]])('%s: botão de atualizar=%s', async (role, allowed) => {
  roles = [role]; await open(); expect(Boolean(screen.queryByRole('button', { name: 'Atualizar do AdsPower' }))).toBe(allowed)
})
test('atualizar força nova leitura e substitui os números', async () => {
  await open(); fireEvent.click(screen.getByRole('button', { name: 'Atualizar do AdsPower' }))
  await waitFor(() => expect(screen.getByText('Perfis', { selector: 'p' }).nextSibling).toHaveTextContent('7')); expect(refreshes).toBe(1); expect(reads).toBe(1)
})
test('falha do AdsPower mostra o motivo sanitizado e permite tentar de novo', async () => {
  readResponse = () => json({ title: 'Não foi possível falar com o AdsPower. Abra o aplicativo e tente novamente.', code: 'Unreachable', status: 503 }, 503)
  render(<App />); expect(await screen.findByRole('alert')).toHaveTextContent('Abra o aplicativo'); readResponse = () => json(dashboard)
  fireEvent.click(screen.getByRole('button', { name: 'Tentar novamente' })); await screen.findByLabelText('03 ESTEIRA DE PERFIL: 2 perfis'); expect(reads).toBe(2)
})
test('falha ao atualizar mantém os dados anteriores e informa o erro', async () => {
  refreshResponse = () => json({ title: 'O AdsPower recusou a chave de API. Confira a chave configurada.', code: 'Unauthorized' }, 502)
  await open(); fireEvent.click(screen.getByRole('button', { name: 'Atualizar do AdsPower' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('recusou a chave'); expect(screen.getByLabelText('03 ESTEIRA DE PERFIL: 2 perfis')).toBeInTheDocument()
})
test('AdsPower sem perfis mostra estado vazio e resposta incompatível mostra erro genérico', async () => {
  readResponse = () => json({ ...dashboard, total: 0, groups: [], tags: [], profiles: [] }); render(<App />)
  await screen.findByRole('heading', { name: 'Nenhum perfil encontrado no AdsPower' }); cleanup()
  readResponse = () => json({ unexpected: true }); render(<App />); expect(await screen.findByRole('alert')).toHaveTextContent(/incompatível|Não foi possível/)
})
test('menu lateral leva ao dashboard', async () => {
  window.history.replaceState({}, '', '/'); render(<App />); fireEvent.click(await screen.findByRole('link', { name: 'Dashboard' })); await screen.findByRole('heading', { name: 'Dashboard' })
})
