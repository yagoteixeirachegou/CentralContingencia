import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { App } from '../../app/App'

const json = (data: unknown, code = 200) => new Response(JSON.stringify(data), { status: code, headers: { 'Content-Type': 'application/json' } })
const task = (id: string, status: string, extra: Record<string, unknown> = {}) => ({
  id, type: 'SimulatedProcessing', status, createdAt: '2026-10-21T12:00:00Z', startedAt: null, completedAt: null, nextAttemptAt: null, lastActivityAt: '2026-10-21T12:01:00Z',
  attemptCount: 0, maxAttempts: 3, lastErrorCode: null, correlationId: 'c', cancelRequested: false, executorId: null, executorName: null, ...extra,
})
const A = 'aaaaaaaa-0000-4000-8000-000000000001', B = 'bbbbbbbb-0000-4000-8000-000000000002', C = 'cccccccc-0000-4000-8000-000000000003'
let role = 'Operator'
let calls: { url: string; method: string }[] = []
let tasks: unknown[]
beforeEach(() => {
  role = 'Operator'; calls = []
  tasks = [task(A, 'Running', { attemptCount: 1, executorId: '11111111-1111-4111-8111-111111111111', executorName: 'win-adspower-01' }), task(B, 'DeadLetter', { attemptCount: 3, lastErrorCode: 'simulated_transient' }), task(C, 'Pending')]
  window.history.replaceState({}, '', '/tasks')
  vi.stubGlobal('fetch', vi.fn(async (input: string, init?: RequestInit) => {
    const url = new URL(input, 'https://localhost'); calls.push({ url: url.pathname + url.search, method: init?.method ?? 'GET' })
    if (url.pathname.endsWith('/me')) return json({ id: 'u1', email: 'synthetic@example.test', roles: [role] })
    if (url.pathname.endsWith('/csrf')) return json({ token: 'synthetic-csrf' })
    if (url.pathname.endsWith('/cancel')) return json(task(A, 'Running', { cancelRequested: true }))
    if (url.pathname === '/api/v1/tasks') {
      const status = url.searchParams.get('status'); const items = status ? (tasks as { status: string }[]).filter(item => item.status === status) : tasks
      return json({ items, page: 1, pageSize: 20, total: items.length })
    }
    return new Response('', { status: 404 })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

test('lista tarefas com estado, executor, tentativas e código de erro sanitizado', async () => {
  render(<App />)
  const table = await screen.findByRole('table')
  const running = within(table).getByText('aaaaaaaa').closest('tr')!
  expect(within(running).getByText('Em execução')).toBeInTheDocument(); expect(within(running).getByText('win-adspower-01')).toBeInTheDocument(); expect(within(running).getByText('1/3')).toBeInTheDocument()
  const dead = within(table).getByText('bbbbbbbb').closest('tr')!
  expect(within(dead).getByText('Esgotada (dead-letter)')).toBeInTheDocument(); expect(within(dead).getByText('simulated_transient')).toBeInTheDocument()
  expect(within(table).getByText('Na fila')).toBeInTheDocument()
  expect(document.body.textContent).not.toMatch(/workerId|credential/i)
})

test('Operator só consulta: não vê o botão de cancelar', async () => {
  render(<App />); await screen.findByRole('table')
  expect(screen.queryByRole('button', { name: /Cancelar tarefa/ })).not.toBeInTheDocument()
})

test('Manager cancela com confirmação humana e o pedido usa CSRF', async () => {
  role = 'Manager'
  render(<App />); await screen.findByRole('table')
  expect(screen.getByRole('button', { name: 'Cancelar tarefa cccccccc' })).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Cancelar tarefa aaaaaaaa' }))
  expect(calls.some(call => call.url.endsWith('/cancel'))).toBe(false) // nada sai antes da confirmação
  const dialog = await screen.findByRole('dialog')
  expect(dialog).toHaveTextContent('interrompe no próximo heartbeat')
  fireEvent.click(within(dialog).getByRole('button', { name: 'Cancelar tarefa' }))
  await waitFor(() => expect(calls.some(call => call.url === `/api/v1/tasks/${A}/cancel` && call.method === 'POST')).toBe(true))
  expect(calls.some(call => call.url === '/api/v1/auth/csrf')).toBe(true)
  expect(await screen.findByRole('status')).toHaveTextContent('Cancelamento solicitado')
})

test('filtro por estado é enviado ao servidor', async () => {
  render(<App />); await screen.findByRole('table')
  fireEvent.change(screen.getByLabelText('Estado'), { target: { value: 'DeadLetter' } })
  await waitFor(() => expect(calls.some(call => call.url.includes('status=DeadLetter'))).toBe(true))
  await waitFor(() => expect(screen.queryByText('aaaaaaaa')).not.toBeInTheDocument())
})

test('mostra estado vazio', async () => {
  tasks = []
  render(<App />)
  expect(await screen.findByText(/Nenhuma tarefa registrada/)).toBeInTheDocument()
})

test('mostra erro com nova tentativa', async () => {
  const original = fetch
  vi.stubGlobal('fetch', vi.fn(async (input: string, init?: RequestInit) => new URL(input, 'https://localhost').pathname === '/api/v1/tasks' ? json({}, 503) : original(input, init)))
  render(<App />)
  expect(await screen.findByRole('alert')).toHaveTextContent('Não foi possível')
  expect(screen.getByRole('button', { name: 'Tentar novamente' })).toBeInTheDocument()
})
