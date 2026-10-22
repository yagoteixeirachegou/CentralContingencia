import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { App } from '../../app/App'
import { buildVerificationPrompt, findDefaultGroup } from './verificationPrompt'
const json = (data: unknown, code = 200) => new Response(JSON.stringify(data), { status: code, headers: { 'Content-Type': 'application/json' } })
const profile = (profileId: string, name: string, groupId: string, groupName: string) => ({ profileId, name, groupId, groupName, tags: [], createdAt: null, serialNumber: profileId.slice(1) })
const dashboard = { generatedAt: '2026-10-16T15:00:00Z', total: 3, fromCache: false,
  groups: [{ groupId: '1', groupName: 'FB | ESTEIRA DE PERFIS', total: 2, oldestCreatedAt: null, newestCreatedAt: null }, { groupId: '2', groupName: 'OUTRO GRUPO', total: 1, oldestCreatedAt: null, newestCreatedAt: null }],
  tags: [], profiles: [profile('p1', 'Perfil 1256', '1', 'FB | ESTEIRA DE PERFIS'), profile('p2', 'Perfil 1257', '1', 'FB | ESTEIRA DE PERFIS'), profile('p3', 'Perfil 9', '2', 'OUTRO GRUPO')] }
let writeText: ReturnType<typeof vi.fn>
beforeEach(() => {
  window.history.replaceState({}, '', '/warmup'); writeText = vi.fn(async () => undefined)
  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
  vi.stubGlobal('fetch', vi.fn(async (input: string) => {
    const url = new URL(input, 'https://localhost')
    if (url.pathname.endsWith('/me')) return json({ id: 'u1', email: 'synthetic@example.test', roles: ['Operator'] })
    if (url.pathname.endsWith('/csrf')) return json({ token: 'synthetic-csrf' })
    if (url.pathname.endsWith('/adspower/dashboard')) return json(dashboard)
    return new Response('', { status: 404 })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

test('lista só os perfis do grupo FB | ESTEIRA DE PERFIS e copia o prompt dos selecionados', async () => {
  render(<App />)
  await screen.findByRole('heading', { name: 'Aquecimento' })
  const table = await screen.findByRole('table')
  expect(within(table).getByText('Perfil 1256')).toBeInTheDocument(); expect(within(table).queryByText('Perfil 9')).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: /Copiar prompt/ })).toBeDisabled()
  fireEvent.click(screen.getByLabelText('Selecionar Perfil 1257'))
  expect(screen.getByRole('status')).toHaveTextContent('1 de 2 selecionados')
  fireEvent.click(screen.getByRole('button', { name: /Copiar prompt/ }))
  await waitFor(() => expect(writeText).toHaveBeenCalledTimes(1))
  const text = writeText.mock.calls[0][0] as string
  expect(text).toContain('Perfil 1257'); expect(text).not.toContain('Perfil 1256'); expect(text).toContain('Não role')
})
test('selecionar todos marca os perfis listados', async () => {
  render(<App />); await screen.findByRole('table')
  fireEvent.click(screen.getByLabelText('Selecionar todos os perfis listados'))
  expect(screen.getByRole('status')).toHaveTextContent('2 de 2 selecionados')
})
test('findDefaultGroup ignora maiúsculas e acentos; prompt não pede interação', () => {
  expect(findDefaultGroup([{ groupName: 'fb | esteira de perfis' }])).toBeDefined(); expect(findDefaultGroup([{ groupName: '03 ESTEIRA DE PERFIL' }])).toBeUndefined()
  const prompt = buildVerificationPrompt([{ name: 'Perfil 1', groupName: 'G' }])
  expect(prompt).toContain('NÃO feche o navegador'); expect(prompt).not.toMatch(/Reels, (curta|assista)/i)
})
