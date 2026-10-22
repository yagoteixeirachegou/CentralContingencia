import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { App } from '../../app/App'

const json = (data: unknown, code = 200) => new Response(JSON.stringify(data), { status: code, headers: { 'Content-Type': 'application/json' } })
const executor = (id: string, name: string, extra: Record<string, unknown> = {}) => ({
  id, name, status: 'Active', connection: 'Online', state: 'Busy', lastSeenAt: '2026-10-21T12:00:00Z', workerVersion: '1.2.3', allowedTypes: ['SimulatedProcessing'],
  createdAt: '2026-10-20T12:00:00Z', credentialRotatedAt: null, revokedAt: null, runningTasks: 2, ...extra,
})
const A = '11111111-1111-4111-8111-111111111111', B = '22222222-2222-4222-8222-222222222222', C = '33333333-3333-4333-8333-333333333333'
const SECRET = 'ce1.11111111111141118111111111111111.SYNTHETICSECRETSYNTHETICSECRETSYNTHETIC1'
let role = 'Admin'
let calls: { url: string; method: string; body?: string }[] = []
let writeText: ReturnType<typeof vi.fn>
beforeEach(() => {
  role = 'Admin'; calls = []; writeText = vi.fn(async () => undefined)
  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
  window.history.replaceState({}, '', '/tasks/executors')
  vi.stubGlobal('fetch', vi.fn(async (input: string, init?: RequestInit) => {
    const url = new URL(input, 'https://localhost'); calls.push({ url: url.pathname, method: init?.method ?? 'GET', body: init?.body as string | undefined })
    if (url.pathname.endsWith('/me')) return json({ id: 'u1', email: 'synthetic@example.test', roles: [role] })
    if (url.pathname.endsWith('/csrf')) return json({ token: 'synthetic-csrf' })
    if (url.pathname === '/api/v1/executors' && init?.method === 'POST') return json({ executor: executor(A, 'novo-worker', { connection: 'Offline', state: 'Offline', lastSeenAt: null, workerVersion: null, runningTasks: 0 }), credential: SECRET }, 201)
    if (url.pathname === '/api/v1/executors') return json([executor(A, 'win-adspower-01'), executor(B, 'win-adspower-02', { connection: 'Offline', state: 'Offline', runningTasks: 0 }),
      executor(C, 'antigo', { status: 'Revoked', connection: 'Revoked', state: 'Offline', revokedAt: '2026-10-21T10:00:00Z', runningTasks: 0 })])
    if (url.pathname.endsWith('/rotate')) return json({ executor: executor(A, 'win-adspower-01'), credential: SECRET })
    if (url.pathname.endsWith('/revoke')) return json(executor(A, 'win-adspower-01', { status: 'Revoked', connection: 'Revoked' }))
    return new Response('', { status: 404 })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

test('mostra executores online, offline e revogados sem expor credenciais', async () => {
  render(<App />)
  const table = await screen.findByRole('table')
  const online = within(table).getByText('win-adspower-01').closest('tr')!
  expect(within(online).getByText('Online')).toBeInTheDocument(); expect(within(online).getByText('Ocupado')).toBeInTheDocument(); expect(within(online).getByText('1.2.3')).toBeInTheDocument()
  expect(within(within(table).getByText('win-adspower-02').closest('tr')!).getAllByText('Offline').length).toBeGreaterThan(0)
  expect(within(within(table).getByText('antigo').closest('tr')!).getByText('Revogado')).toBeInTheDocument()
  expect(document.body.textContent).not.toContain('ce1.'); expect(document.body.textContent).not.toMatch(/credentialHash/i)
})

test('Operator e Manager não veem ações administrativas', async () => {
  for (const current of ['Operator', 'Manager']) {
    role = current; cleanup(); render(<App />); await screen.findByRole('table')
    expect(screen.queryByRole('button', { name: 'Novo executor' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Revogar/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Rotacionar/ })).not.toBeInTheDocument()
  }
})

test('Admin cria executor e a credencial aparece uma única vez, só na memória da tela', async () => {
  render(<App />); await screen.findByRole('table')
  fireEvent.click(screen.getByRole('button', { name: 'Novo executor' }))
  const form = await screen.findByRole('dialog')
  expect(within(form).getByRole('button', { name: 'Criar executor' })).toBeDisabled()
  fireEvent.change(within(form).getByLabelText('Nome do executor'), { target: { value: 'novo-worker' } })
  fireEvent.click(within(form).getByRole('button', { name: 'Criar executor' }))
  const reveal = await screen.findByRole('dialog', { name: 'Credencial do executor' })
  expect(within(reveal).getByLabelText('Credencial')).toHaveValue(SECRET)
  expect(calls.find(call => call.method === 'POST' && call.url === '/api/v1/executors')?.body).toBe(JSON.stringify({ name: 'novo-worker' }))
  fireEvent.click(within(reveal).getByRole('button', { name: /Copiar credencial/ }))
  await waitFor(() => expect(writeText).toHaveBeenCalledWith(SECRET))
  fireEvent.click(within(reveal).getByRole('button', { name: 'Já guardei' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  expect(document.body.textContent).not.toContain(SECRET)
  expect(Object.values(localStorage).some(value => String(value).includes('ce1.'))).toBe(false)
  expect(Object.values(sessionStorage).some(value => String(value).includes('ce1.'))).toBe(false)
})

test('revogar exige confirmação e rotacionar mostra a nova credencial', async () => {
  render(<App />); await screen.findByRole('table')
  fireEvent.click(screen.getByRole('button', { name: 'Revogar win-adspower-01' }))
  expect(calls.some(call => call.url.endsWith('/revoke'))).toBe(false)
  const confirm = await screen.findByRole('dialog')
  expect(confirm).toHaveTextContent('definitiva')
  fireEvent.click(within(confirm).getByRole('button', { name: 'Revogar' }))
  await waitFor(() => expect(calls.some(call => call.url === `/api/v1/executors/${A}/revoke` && call.method === 'POST')).toBe(true))
  expect(await screen.findByRole('status')).toHaveTextContent('Executor revogado')

  fireEvent.click(screen.getByRole('button', { name: 'Rotacionar credencial de win-adspower-01' }))
  fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Rotacionar' }))
  expect(await screen.findByLabelText('Credencial')).toHaveValue(SECRET)
})

test('estado vazio orienta o Admin', async () => {
  const original = fetch
  vi.stubGlobal('fetch', vi.fn(async (input: string, init?: RequestInit) => new URL(input, 'https://localhost').pathname === '/api/v1/executors' && init?.method !== 'POST' ? json([]) : original(input, init)))
  render(<App />)
  expect(await screen.findByText(/Nenhum executor provisionado/)).toBeInTheDocument()
})
