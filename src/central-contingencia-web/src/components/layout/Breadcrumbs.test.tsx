import { cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, test } from 'vitest'
import { Breadcrumbs } from './Breadcrumbs'
import { trailFor } from './trail'

afterEach(cleanup)
const labels = (path: string, grid = '/proxies') => trailFor(path, grid).map(crumb => crumb.label)

test('cada rota tem a trilha esperada, sempre a partir do Início', () => {
  expect(labels('/')).toEqual(['Início'])
  expect(labels('/dashboard')).toEqual(['Início', 'Dashboard'])
  expect(labels('/proxies')).toEqual(['Início', 'HTTP - FACEBOOK', 'Proxies'])
  expect(labels('/proxies/import')).toEqual(['Início', 'HTTP - FACEBOOK', 'Importar CSV'])
  expect(labels('/proxies/ingestion')).toEqual(['Início', 'HTTP - FACEBOOK', 'Perfis', 'Novos perfis (TXT)'])
  expect(labels('/profiles')).toEqual(['Início', 'HTTP - FACEBOOK', 'Perfis'])
  expect(labels('/profiles/abc')).toEqual(['Início', 'HTTP - FACEBOOK', 'Perfis', 'Detalhes do perfil'])
  expect(labels('/administration')).toEqual(['Início', 'Administração'])
})

test('a última migalha é a página atual e as anteriores são links', () => {
  render(<MemoryRouter initialEntries={['/proxies/import']}><Breadcrumbs /></MemoryRouter>)
  expect(screen.getByRole('link', { name: 'Início' }).getAttribute('href')).toBe('/')
  expect(screen.getByRole('link', { name: 'HTTP - FACEBOOK' }).getAttribute('href')).toBe('/proxies')
  expect(screen.getByText('Importar CSV').getAttribute('aria-current')).toBe('page')
  expect(screen.queryByRole('link', { name: 'Importar CSV' })).toBeNull()
})

test('o link da planilha preserva os filtros do grid recebidos na navegação', () => {
  render(<MemoryRouter initialEntries={[{ pathname: '/profiles', state: { returnTo: '/proxies?status=Active' } }]}><Breadcrumbs /></MemoryRouter>)
  expect(screen.getByRole('link', { name: 'HTTP - FACEBOOK' }).getAttribute('href')).toBe('/proxies?status=Active')
})

test('no Início não há links, só a página atual', () => {
  render(<MemoryRouter initialEntries={['/']}><Breadcrumbs /></MemoryRouter>)
  expect(screen.queryAllByRole('link')).toHaveLength(0); expect(screen.getByText('Início').getAttribute('aria-current')).toBe('page')
})
