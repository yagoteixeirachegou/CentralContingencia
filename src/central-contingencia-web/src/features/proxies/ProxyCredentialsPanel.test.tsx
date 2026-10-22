import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { AuthContext, type AuthState } from '../auth/auth-context'
import { ProxyCredentialsPanel } from './ProxyCredentialsPanel'

const secret = 'synthetic-panel-secret'
let reveals: Request[] = []
let revealStatus = 200
const write = vi.fn(async () => undefined)
function auth(roles: string[]): AuthState {
  return { user: { id: 'u1', email: 'admin@example.test', roles }, loading: false, error: '', notice: '', login: async () => {}, logout: async () => {}, refresh: async () => {} }
}
const show = (roles = ['Admin']) => render(<AuthContext.Provider value={auth(roles)}><ProxyCredentialsPanel proxyId="p1" host="h.example.test" port={8080} /></AuthContext.Provider>)
beforeEach(() => {
  reveals = []; revealStatus = 200; write.mockClear()
  Object.defineProperty(navigator, 'clipboard', { value: { writeText: write }, configurable: true })
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    if (url.endsWith('/csrf')) return new Response(JSON.stringify({ token: 'csrf' }))
    reveals.push(new Request('https://x.test' + url, { method: init?.method, headers: init?.headers }))
    return revealStatus === 200 ? new Response(JSON.stringify({ id: 'p1', host: 'h.example.test', port: 8080, username: 'synthetic-user', password: secret })) : new Response('', { status: revealStatus })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

test('senha começa oculta, o olho revela com CSRF e ocultar apaga o valor', async () => {
  show()
  expect(document.body.textContent).not.toContain(secret)
  fireEvent.click(screen.getByRole('button', { name: 'Mostrar usuário e senha' }))
  expect(await screen.findByTestId('proxy-password')).toHaveTextContent(secret)
  expect(reveals).toHaveLength(1); expect(reveals[0].method).toBe('POST'); expect(reveals[0].url).toContain('/proxies/p1/credentials/reveal')
  expect(reveals[0].headers.get('X-CSRF-TOKEN')).toBe('csrf')
  fireEvent.click(screen.getByRole('button', { name: 'Ocultar' }))
  expect(document.body.textContent).not.toContain(secret)
})
test('copiar tudo copia host:porta:usuário:senha mesmo sem ter revelado antes', async () => {
  show()
  fireEvent.click(screen.getByRole('button', { name: 'Copiar tudo' }))
  await waitFor(() => expect(write).toHaveBeenCalledWith(`h.example.test:8080:synthetic-user:${secret}`))
  expect(await screen.findByRole('button', { name: 'Copiado' })).toBeInTheDocument()
})
test('papéis sem Admin não veem botões nem consultam a API', () => {
  show(['Manager'])
  expect(screen.queryByRole('button', { name: /Mostrar|Copiar/ })).toBeNull()
  expect(screen.getByText(/somente Admin/)).toBeInTheDocument(); expect(reveals).toHaveLength(0)
})
test('falha da API mostra aviso e nunca exibe valores', async () => {
  revealStatus = 403
  show()
  fireEvent.click(screen.getByRole('button', { name: 'Mostrar usuário e senha' }))
  expect(await screen.findByRole('alert')).toBeInTheDocument()
  expect(document.body.textContent).not.toContain(secret)
})
