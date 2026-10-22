import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { App } from '../../app/App'
import type { Allocation, OperationalProfile } from './contracts'
const profileId = '00000000-0000-4000-8000-000000000001'
const proxyId = '00000000-0000-4000-8000-000000000002'
const allocationId = '00000000-0000-4000-8000-000000000003'
const baseProfile: OperationalProfile = { id: profileId, name: 'Perfil sintético', platform: 'Plataforma sintética', status: 'Active', createdAt: '2026-10-11T00:00:00Z', updatedAt: '2026-10-11T00:00:00Z', closedAt: null }
const baseAllocation: Allocation = { id: allocationId, profileId, proxyId, status: 'Reserved', createdAt: '2026-10-11T00:00:00Z', updatedAt: '2026-10-11T00:00:00Z', reservationExpiresAt: '2099-01-01T12:00:00Z', confirmedAt: null, endedAt: null, endReason: null }
let profiles: OperationalProfile[]; let allocations: Allocation[]; let roles: string[]; let session: boolean
let readStatus: number; let writeStatus: number; let currentStatus: number; let available: boolean; let delayWrite: boolean; let delayRead: boolean
let requests: { url: URL; options?: RequestInit }[]
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
const empty = (status: number) => new Response(null, { status })
const writes = () => requests.filter(item => item.options?.method === 'POST' && !item.url.pathname.includes('/auth/'))
beforeEach(() => {
  profiles = [{ ...baseProfile }]; allocations = []; roles = ['Admin']; session = true
  readStatus = 200; writeStatus = 200; currentStatus = 200; available = true; delayWrite = false; delayRead = false; requests = []
  window.history.replaceState({}, '', '/profiles')
  vi.stubGlobal('fetch', vi.fn(async (input: string, options?: RequestInit) => {
    const url = new URL(input, 'https://localhost'); requests.push({ url, options })
    const path = url.pathname
    if (path.endsWith('/csrf')) return json({ token: 'synthetic-csrf-secret' })
    if (path.endsWith('/me')) return session ? json({ id: 'synthetic-admin', email: 'admin@example.test', roles }) : empty(401)
    if (path.endsWith('/logout')) { session = false; return empty(204) }
    if (options?.method === 'POST') {
      if (delayWrite) await new Promise(resolve => setTimeout(resolve, 100))
      if (writeStatus !== 200) return empty(writeStatus)
      const body = JSON.parse(String(options.body)) as Record<string, string>
      if (path === '/api/v1/profiles') { const created = { ...baseProfile, id: '00000000-0000-4000-8000-000000000099', name: body.name, platform: body.platform }; profiles.push(created); return json(created, 201) }
      if (path.endsWith('/reserve')) { const allocation = { ...baseAllocation, profileId: body.profileId, proxyId: body.proxyId }; allocations.push(allocation); available = false; return json(allocation, 201) }
      const item = allocations.find(row => path.includes(row.id))
      if (!item) return empty(404)
      if (path.endsWith('/confirm')) { item.status = 'Confirmed'; item.confirmedAt = '2026-10-11T00:01:00Z'; return empty(204) }
      if (path.endsWith('/release')) { item.status = 'Released'; item.endReason = 'Released'; item.endedAt = '2026-10-11T00:02:00Z'; available = true; return empty(204) }
    }
    if (path.includes('/profiles') && readStatus !== 200) return empty(readStatus)
    if (path === '/api/v1/allocations') {
      if (currentStatus !== 200) return empty(currentStatus)
      const items = allocations.filter(row => row.profileId === url.searchParams.get('profileId') && row.status === url.searchParams.get('status'))
      return json({ items: items.slice(0, 1), total: items.length, page: 1, pageSize: 1 })
    }
    if (/\/profiles\/[^/]+\/allocations$/.test(path)) {
      const status = url.searchParams.get('status')
      const items = allocations.filter(row => row.profileId === path.split('/')[4] && (!status || row.status === status))
      return json({ items, total: items.length, page: Number(url.searchParams.get('page')), pageSize: Number(url.searchParams.get('pageSize')) })
    }
    if (path === '/api/v1/profiles') {
      if (delayRead) await new Promise(resolve => setTimeout(resolve, 50))
      return json({ items: profiles, total: profiles.length ? 61 : 0, page: Number(url.searchParams.get('page')), pageSize: Number(url.searchParams.get('pageSize')) })
    }
    if (path.startsWith('/api/v1/profiles/')) return profiles.find(row => path.endsWith(row.id)) ? json(profiles.find(row => path.endsWith(row.id))) : empty(404)
    if (path === '/api/v1/proxies/available') return json({ items: available ? [{ id: proxyId, host: 'synthetic.proxy.test', port: 8080, region: 'BR', usernameMasked: '***', status: 'Active', available: true, expiresAt: '2099-01-01T12:00:00Z', createdAt: baseProfile.createdAt, updatedAt: baseProfile.updatedAt }] : [], page: 1, pageSize: 20, total: available ? 1 : 0 })
    if (path.startsWith('/api/v1/proxies/')) return json({ id: proxyId, host: 'synthetic.proxy.test', port: 8080, region: 'BR', usernameMasked: '***', username: 'synthetic-sensitive-user', password: 'synthetic-persisted-password', available, status: 'Active', expiresAt: '2099-01-01T12:00:00Z' })
    return empty(404)
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })
async function list() { render(<App />); await screen.findByRole('link', { name: baseProfile.name }) }
async function detail() { window.history.replaceState({}, '', `/profiles/${profileId}`); render(<App />); await screen.findByRole('heading', { name: baseProfile.name }); await waitFor(() => expect(screen.queryByText('Carregando alocação atual…')).not.toBeInTheDocument()) }
async function reserve() {
  fireEvent.click(await screen.findByRole('button', { name: 'Reservar proxy' }))
  fireEvent.click(await screen.findByRole('radio', { name: 'Selecionar synthetic.proxy.test:8080' }))
  fireEvent.click(screen.getByRole('button', { name: 'Reservar proxy selecionada' }))
  await screen.findByText('Proxy reservada. Confira o prazo antes de confirmar.')
}

test.each([['', ''], ['DARKPOST', ''], ['', 'TIKTOK'], ['DARKPOST', 'TIKTOK']])('cadastro suporta atribuições opcionais %s e %s sem plataforma legada artificial', async (first, second) => {
  await list(); fireEvent.click(screen.getByRole('button', { name: 'Novo perfil' }))
  fireEvent.change(screen.getByLabelText('Nome do perfil'), { target: { value: 'Atribuições sintéticas' } })
  fireEvent.change(screen.getByLabelText('Plataforma 1'), { target: { value: first } }); fireEvent.change(screen.getByLabelText('Plataforma 2'), { target: { value: second } })
  fireEvent.click(screen.getByRole('button', { name: 'Cadastrar perfil' })); await screen.findByRole('heading', { name: 'Atribuições sintéticas' })
  expect(JSON.parse(String(writes()[0].options?.body))).toEqual({ name: 'Atribuições sintéticas', ...(first ? { platform1: first } : {}), ...(second ? { platform2: second } : {}) })
})

test('listagem envia filtros exatos, ordenação e paginação sem filtrar apenas a página', async () => {
  await list(); fireEvent.change(screen.getByLabelText('Nome exato'), { target: { value: baseProfile.name } }); fireEvent.change(screen.getByLabelText('Plataforma exata'), { target: { value: baseProfile.platform } }); fireEvent.change(screen.getByLabelText('Status do perfil'), { target: { value: 'Active' } }); fireEvent.click(screen.getByRole('button', { name: 'Aplicar filtros' }))
  await waitFor(() => expect(requests.some(r => r.url.searchParams.get('name') === baseProfile.name && r.url.searchParams.get('platform') === baseProfile.platform && r.url.searchParams.get('status') === 'Active')).toBe(true))
  fireEvent.change(screen.getByLabelText('Ordenar por'), { target: { value: 'name' } }); fireEvent.click(screen.getByRole('button', { name: 'Decrescente' }))
  await waitFor(() => expect(requests.some(r => r.url.searchParams.get('sort') === 'name' && r.url.searchParams.get('direction') === 'asc')).toBe(true))
  await waitFor(() => expect(screen.getByRole('button', { name: 'Próxima página de perfis' })).not.toBeDisabled()); fireEvent.click(screen.getByRole('button', { name: 'Próxima página de perfis' }))
  await waitFor(() => expect(requests.some(r => r.url.searchParams.get('page') === '2')).toBe(true))
  fireEvent.change(await screen.findByLabelText('Por página'), { target: { value: '100' } }); await waitFor(() => expect(requests.some(r => r.url.searchParams.get('pageSize') === '100' && r.url.searchParams.get('page') === '1')).toBe(true))
})
test('cadastro valida campos e usa CSRF compartilhado; navega para detalhes', async () => {
  await list(); fireEvent.click(screen.getByRole('button', { name: 'Novo perfil' })); fireEvent.click(screen.getByRole('button', { name: 'Cadastrar perfil' })); await screen.findByText('Informe o nome.'); expect(writes()).toHaveLength(0)
  fireEvent.change(screen.getByLabelText('Nome do perfil'), { target: { value: ' Novo sintético ' } }); fireEvent.change(screen.getByLabelText('Plataforma'), { target: { value: ' Outra ' } }); fireEvent.click(screen.getByRole('button', { name: 'Cadastrar perfil' }))
  await screen.findByRole('heading', { name: 'Novo sintético' }); expect(writes()).toHaveLength(1); expect(JSON.parse(String(writes()[0].options?.body))).toEqual({ name: 'Novo sintético', platform: 'Outra' }); expect(writes()[0].options?.headers).toMatchObject({ 'X-CSRF-TOKEN': 'synthetic-csrf-secret' })
})
test('INFOPRODUTO está disponível em ambos os slots sem preenchimento automático', async () => {
  await list(); fireEvent.click(screen.getByRole('button', { name: 'Novo perfil' }))
  const first = screen.getByLabelText('Plataforma 1'); const second = screen.getByLabelText('Plataforma 2')
  expect(within(first).getByRole('option', { name: 'INFOPRODUTO' })).toBeInTheDocument()
  expect(within(second).getByRole('option', { name: 'INFOPRODUTO' })).toBeInTheDocument()
  expect(first).toHaveValue(''); expect(second).toHaveValue('')
  fireEvent.change(screen.getByLabelText('Nome do perfil'), { target: { value: 'Infoproduto sintético' } })
  fireEvent.change(first, { target: { value: 'INFOPRODUTO' } })
  fireEvent.click(screen.getByRole('button', { name: 'Cadastrar perfil' }))
  await screen.findByRole('heading', { name: 'Infoproduto sintético' })
  expect(JSON.parse(String(writes()[0].options?.body))).toEqual({ name: 'Infoproduto sintético', platform1: 'INFOPRODUTO' })
})
test('reserva usa UUID idempotente e TTL do backend, confirma, libera com confirmação e reutiliza', async () => {
  const storage = vi.spyOn(Storage.prototype, 'setItem'); await detail(); await reserve()
  const request = writes()[0]; expect(JSON.parse(String(request.options?.body))).toEqual({ profileId, proxyId }); expect(request.options?.headers).toMatchObject({ 'Idempotency-Key': expect.stringMatching(/^[0-9a-f-]{36}$/), 'X-CSRF-TOKEN': 'synthetic-csrf-secret' })
  expect(screen.getByText(/Prazo da reserva \(horário local\)/)).toBeInTheDocument(); fireEvent.click(await screen.findByRole('button', { name: 'Confirmar reserva' })); await screen.findByText('Reserva confirmada.')
  fireEvent.click(await screen.findByRole('button', { name: 'Liberar proxy' })); await screen.findByRole('dialog'); fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancelar' })); expect(writes().filter(r => r.url.pathname.endsWith('/release'))).toHaveLength(0)
  fireEvent.click(screen.getByRole('button', { name: 'Liberar proxy' })); fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Confirmar liberação' })); await screen.findByText(/Alocação liberada/); await screen.findByRole('button', { name: 'Reservar proxy' }); expect(screen.getByText('Liberação')).toBeInTheDocument(); await reserve()
  expect(writes().filter(r => r.url.pathname.endsWith('/reserve'))).toHaveLength(2); expect(storage).not.toHaveBeenCalled()
  const confirm = writes().find(r => r.url.pathname.endsWith('/confirm'))!; expect(JSON.parse(String(confirm.options?.body))).toEqual({})
})
test('detalhes consultam alocação atual independentemente de histórico paginado', async () => {
  allocations = [{ ...baseAllocation, status: 'Confirmed', confirmedAt: baseAllocation.createdAt }]; await detail(); await screen.findByText(/synthetic.proxy.test:8080/)
  expect(requests.some(r => r.url.pathname === '/api/v1/allocations' && r.url.searchParams.get('status') === 'Confirmed' && r.url.searchParams.get('pageSize') === '1')).toBe(true)
  fireEvent.change(screen.getByLabelText('Estado da alocação'), { target: { value: 'Released' } }); await screen.findByText('Nenhuma alocação encontrada.'); expect(screen.getByRole('button', { name: 'Liberar proxy' })).toBeInTheDocument()
})
test('expiração exibida não envia confirmação automática e permite nova seleção pela API', async () => {
  allocations = [{ ...baseAllocation, reservationExpiresAt: '2000-01-01T00:00:00Z' }]; await detail(); await screen.findByText('Reserva com prazo vencido'); expect(screen.queryByRole('button', { name: 'Confirmar reserva' })).not.toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Reservar proxy' })).toBeInTheDocument(); expect(writes()).toHaveLength(0)
})
test('o vencimento do prazo atualiza os controles sem clique ou mutação', async () => {
  allocations = [{ ...baseAllocation, reservationExpiresAt: new Date(Date.now() + 1500).toISOString() }]; await detail(); await screen.findByRole('button', { name: 'Confirmar reserva' }); await screen.findByText('Reserva com prazo vencido', {}, { timeout: 3000 }); expect(screen.queryByRole('button', { name: 'Confirmar reserva' })).not.toBeInTheDocument(); expect(writes()).toHaveLength(0)
})
test.each(['Admin', 'Manager'])('%s tem controles de cadastro e alocação', async role => {
  roles = [role]; await list(); expect(screen.getByRole('button', { name: 'Novo perfil' })).toBeInTheDocument(); fireEvent.click(screen.getByRole('link', { name: baseProfile.name })); expect(await screen.findByRole('button', { name: 'Reservar proxy' })).toBeInTheDocument()
})
test('Operator consulta detalhes e histórico sem controles de mutação', async () => {
  roles = ['Operator']; allocations = [{ ...baseAllocation }]; await detail(); await screen.findByText(/synthetic.proxy.test/); expect(screen.queryByRole('button', { name: 'Confirmar reserva' })).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Liberar proxy' })).not.toBeInTheDocument(); expect(screen.getByRole('heading', { name: 'Histórico de alocações' })).toBeInTheDocument(); expect(writes()).toHaveLength(0)
})
test('papel desconhecido não inicia consultas e não ganha link no menu', async () => {
  roles = ['Integration']; render(<App />); await screen.findByRole('heading', { name: 'Acesso negado' }); expect(requests.some(r => r.url.pathname.includes('/profiles'))).toBe(false); expect(screen.queryByRole('link', { name: 'Perfis' })).not.toBeInTheDocument()
})
test.each([403, 404, 503])('erro HTTP %s é sanitizado em consulta', async status => {
  readStatus = status; window.history.replaceState({}, '', `/profiles/${profileId}`); render(<App />); await screen.findByRole('alert'); expect(screen.queryByRole('button', { name: 'Reservar proxy' })).not.toBeInTheDocument()
})
test('sessão expirada abandona página e cache operacional', async () => {
  readStatus = 401; render(<App />); await screen.findByRole('heading', { name: 'Acesso interno' }); expect(screen.getByText('Sessão expirada. Entre novamente.')).toBeInTheDocument()
})
test.each([409, 500, 503])('falha %s de confirmação atualiza estado e não repete escrita', async status => {
  allocations = [{ ...baseAllocation }]; await detail(); await screen.findByRole('button', { name: 'Confirmar reserva' }); writeStatus = status; const before = requests.length; fireEvent.click(screen.getByRole('button', { name: 'Confirmar reserva' })); await screen.findByRole('alert'); expect(writes()).toHaveLength(1); await waitFor(() => expect(requests.slice(before).some(r => r.url.pathname === '/api/v1/allocations')).toBe(true))
})
test('conflito na reserva limpa seleção e reconsulta disponíveis sem repetir POST', async () => {
  await detail(); writeStatus = 409; fireEvent.click(screen.getByRole('button', { name: 'Reservar proxy' })); fireEvent.click(await screen.findByRole('radio')); fireEvent.click(screen.getByRole('button', { name: 'Reservar proxy selecionada' })); await screen.findByText(/Conflito operacional/); await waitFor(() => expect(screen.getByRole('button', { name: 'Reservar proxy selecionada' })).toBeDisabled()); expect(writes()).toHaveLength(1)
})
test('duplo clique de confirmação não duplica o envio', async () => {
  allocations = [{ ...baseAllocation }]; delayWrite = true; await detail(); const confirm = await screen.findByRole('button', { name: 'Confirmar reserva' }); fireEvent.click(confirm); fireEvent.click(confirm); await screen.findByText('Reserva confirmada.'); expect(writes()).toHaveLength(1)
})
test('loading e lista vazia são explícitos', async () => {
  profiles = []; delayRead = true; render(<App />); await screen.findByText('Carregando perfis…'); await screen.findByText('Nenhum perfil encontrado.')
})
test('perfil encerrado mantém histórico e não oferece novas reservas', async () => {
  profiles[0].status = 'Closed'; allocations = [{ ...baseAllocation, status: 'Released', endReason: 'ProfileClosed', endedAt: baseAllocation.createdAt }]; await detail(); expect(screen.getByText(/Perfil encerrado. O histórico/)).toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Reservar proxy' })).not.toBeInTheDocument(); expect(await screen.findByText('Perfil encerrado')).toBeInTheDocument()
})
test('tokens e campos de credenciais desconhecidos da resposta nunca são renderizados', async () => {
  allocations = [{ ...baseAllocation }]; await detail(); expect(await screen.findByTestId('proxy-username')).toHaveTextContent('••••••••••'); for (const secret of ['synthetic-sensitive-user', 'synthetic-persisted-password', 'synthetic-csrf-secret']) expect(document.body.textContent).not.toContain(secret)
})
test('leituras concorrentes inconsistentes mostram conflito sem escolher vínculo arbitrário', async () => {
  allocations = [{ ...baseAllocation }, { ...baseAllocation, id: '00000000-0000-4000-8000-000000000004', status: 'Confirmed' }]; await detail(); await screen.findByText(/Conflito operacional/); expect(screen.queryByRole('button', { name: 'Liberar proxy' })).not.toBeInTheDocument()
})
test('cadastro com resultado incerto exige conferir lista antes de novo envio', async () => {
  await list(); writeStatus = 503; fireEvent.click(screen.getByRole('button', { name: 'Novo perfil' })); fireEvent.change(screen.getByLabelText('Nome do perfil'), { target: { value: 'Incerto sintético' } }); fireEvent.change(screen.getByLabelText('Plataforma'), { target: { value: 'Sintética' } }); fireEvent.click(screen.getByRole('button', { name: 'Cadastrar perfil' })); await screen.findByText('Feche o formulário e confira a listagem antes de iniciar outro cadastro.'); expect(screen.getByRole('button', { name: 'Cadastrar perfil' })).toBeDisabled(); expect(writes()).toHaveLength(1)
})
