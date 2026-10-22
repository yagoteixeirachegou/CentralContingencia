import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { ThemeToggle } from './theme-toggle'
beforeEach(() => { localStorage.clear(); document.documentElement.classList.remove('dark') })
afterEach(() => { cleanup(); vi.unstubAllGlobals() })
test('alterna entre claro e escuro e guarda a escolha', () => {
  render(<ThemeToggle />)
  fireEvent.click(screen.getByRole('button', { name: 'Ativar modo escuro' }))
  expect(document.documentElement).toHaveClass('dark'); expect(localStorage.getItem('central-theme')).toBe('dark')
  fireEvent.click(screen.getByRole('button', { name: 'Ativar modo claro' }))
  expect(document.documentElement).not.toHaveClass('dark'); expect(localStorage.getItem('central-theme')).toBe('light')
})
test('sem escolha salva, segue a preferência do sistema', () => {
  vi.stubGlobal('matchMedia', () => ({ matches: true, addEventListener: () => undefined, removeEventListener: () => undefined }))
  render(<ThemeToggle />)
  expect(document.documentElement).toHaveClass('dark'); expect(screen.getByRole('button', { name: 'Ativar modo claro' })).toBeInTheDocument()
})
test('escolha salva vence a preferência do sistema', () => {
  localStorage.setItem('central-theme', 'light')
  vi.stubGlobal('matchMedia', () => ({ matches: true, addEventListener: () => undefined, removeEventListener: () => undefined }))
  render(<ThemeToggle />)
  expect(document.documentElement).not.toHaveClass('dark')
})
test('armazenamento indisponível não quebra a alternância', () => {
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked') })
  render(<ThemeToggle />); fireEvent.click(screen.getByRole('button', { name: 'Ativar modo escuro' }))
  expect(document.documentElement).toHaveClass('dark'); vi.restoreAllMocks()
})
