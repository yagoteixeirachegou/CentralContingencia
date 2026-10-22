import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { App } from './App'
const user = { id: 'test-id', email: 'admin@example.test', roles: ['Admin'] }
let session: typeof user | null
let invalid = false
let offline = false
beforeEach(() => {
  session = null; invalid = false; offline = false
  window.history.replaceState({}, '', '/')
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (offline) throw new Error('offline')
    if (url.endsWith('/csrf')) return new Response(JSON.stringify({ token: 'test-csrf' }))
    if (url.endsWith('/login')) {
      if (invalid) return new Response('', { status: 401 })
      session = user; return new Response(null, { status: 204 })
    }
    if (url.endsWith('/logout')) { session = null; return new Response(null, { status: 204 }) }
    return session ? new Response(JSON.stringify(session)) : new Response('', { status: 401 })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals() })
async function enter() {
  await screen.findByRole('heading', { name: 'Acesso interno' })
  fireEvent.change(screen.getByLabelText('E-mail'), { target: { value: user.email } })
  fireEvent.change(screen.getByLabelText('Senha'), { target: { value: 'Example-only!123' } })
  fireEvent.click(screen.getByRole('button', { name: 'Entrar' }))
}
test('rota protegida redireciona para login interno', async () => {
  render(<App />)
  await screen.findByRole('heading', { name: 'Acesso interno' })
  expect(screen.getByText(/Não há cadastro público/)).toBeInTheDocument()
})
test('login válido exibe layout e logout encerra sessão', async () => {
  render(<App />); await enter()
  await screen.findByRole('heading', { name: 'Início' })
  expect(screen.getByRole('navigation', { name: 'Navegação principal' })).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Sair' }))
  await screen.findByRole('heading', { name: 'Acesso interno' })
  expect(screen.getByText('Sessão encerrada.')).toBeInTheDocument()
})
test('credenciais inválidas exibem mensagem clara', async () => {
  invalid = true
  render(<App />); await enter()
  expect(await screen.findByRole('alert')).toHaveTextContent('Credenciais inválidas')
})
test('validação impede envio de formulário vazio', async () => {
  render(<App />)
  await screen.findByRole('heading', { name: 'Acesso interno' })
  fireEvent.click(screen.getByRole('button', { name: 'Entrar' }))
  expect(await screen.findByText('Informe a senha.')).toBeInTheDocument()
  expect(fetch).toHaveBeenCalledTimes(1)
})
test('Operator recebe acesso negado na rota administrativa', async () => {
  session = { ...user, roles: ['Operator'] }
  window.history.replaceState({}, '', '/administration')
  render(<App />)
  expect(await screen.findByRole('heading', { name: 'Acesso negado' })).toBeInTheDocument()
})
test('expiração redireciona para login e informa motivo', async () => {
  session = user
  render(<App />)
  await screen.findByRole('heading', { name: 'Início' })
  act(() => window.dispatchEvent(new Event('session-expired')))
  expect(await screen.findByText('Sessão expirada. Entre novamente.')).toBeInTheDocument()
})
test('falha de rede oferece recuperação sem presumir sessão anônima', async () => {
  offline = true
  render(<App />)
  expect(await screen.findByRole('alert')).toHaveTextContent('Não foi possível verificar a sessão')
  expect(screen.getByRole('button', { name: 'Tentar novamente' })).toBeInTheDocument()
})
test('login volta à página de origem e ignora destinos externos', async () => {
  window.history.replaceState({}, '', '/administration?x=1')
  render(<App />); await enter()
  expect(await screen.findByRole('heading', { name: 'Administração' })).toBeInTheDocument()
  expect(window.location.pathname + window.location.search).toBe('/administration?x=1')
})
test('falha de rede no login mostra mensagem em português', async () => {
  render(<App />)
  await screen.findByRole('heading', { name: 'Acesso interno' })
  offline = true
  fireEvent.change(screen.getByLabelText('E-mail'), { target: { value: user.email } })
  fireEvent.change(screen.getByLabelText('Senha'), { target: { value: 'Example-only!123' } })
  fireEvent.click(screen.getByRole('button', { name: 'Entrar' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Sem conexão com o servidor')
})
test('rota inexistente mostra 404 dentro do layout com caminho de volta', async () => {
  session = user
  window.history.replaceState({}, '', '/nao-existe')
  render(<App />)
  expect(await screen.findByRole('heading', { name: 'Página não encontrada' })).toBeInTheDocument()
  expect(screen.getByRole('navigation', { name: 'Navegação principal' })).toBeInTheDocument()
  expect(screen.getByRole('link', { name: /Ir para o início/ })).toBeInTheDocument()
})
test('acesso negado mostra o papel atual e link para o início', async () => {
  session = { ...user, roles: ['Operator'] }
  window.history.replaceState({}, '', '/administration')
  render(<App />)
  await screen.findByRole('heading', { name: 'Acesso negado' })
  expect(screen.getByText(/Seu papel atual: Operator/)).toBeInTheDocument()
  expect(screen.getByRole('link', { name: /Voltar ao início/ })).toBeInTheDocument()
})
test('menu recolhível e botão de mostrar senha', async () => {
  render(<App />)
  await screen.findByRole('heading', { name: 'Acesso interno' })
  const senha = screen.getByLabelText('Senha')
  fireEvent.click(screen.getByRole('button', { name: 'Mostrar senha' }))
  expect(senha).toHaveAttribute('type', 'text')
  fireEvent.click(screen.getByRole('button', { name: 'Ocultar senha' }))
  expect(senha).toHaveAttribute('type', 'password')
})
test('marca oficial aparece no login', async () => {
  render(<App />)
  await screen.findByRole('heading', { name: 'Acesso interno' })
  expect(document.body.textContent).toContain('ChegouContingência')
})
test('menu lateral recolhe só com ícones e lembra a escolha', async () => {
  localStorage.removeItem('central-menu')
  session = user
  render(<App />)
  await screen.findByRole('heading', { name: 'Início' })
  fireEvent.click(screen.getByRole('button', { name: 'Recolher menu' }))
  expect(localStorage.getItem('central-menu')).toBe('collapsed')
  expect(screen.getByRole('button', { name: 'Expandir menu' })).toHaveAttribute('aria-expanded', 'false')
  expect(screen.getByRole('link', { name: 'Dashboard' })).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Expandir menu' }))
  expect(localStorage.getItem('central-menu')).toBe('expanded')
})
