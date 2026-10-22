import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, expect, test } from 'vitest'
import { CardsSkeleton, ListSkeleton, PageSkeleton, TableSkeleton } from './skeleton'

afterEach(cleanup)

test('o carregamento é anunciado uma vez por região e as silhuetas ficam ocultas aos leitores de tela', () => {
  render(<TableSkeleton label="Carregando proxies…" columns={3} rows={2} />)
  const region = screen.getByRole('status'); expect(region.getAttribute('aria-busy')).toBe('true')
  expect(screen.getByText('Carregando proxies…').className).toContain('sr-only')
  expect(region.querySelectorAll('[aria-hidden="true"]')).toHaveLength(3 + 3 * 2)
})

test('cartões, lista e página geram regiões de carregamento rotuladas', () => {
  render(<><CardsSkeleton label="A" cards={3} /><ListSkeleton label="B" rows={2} /><PageSkeleton label="C" /></>)
  expect(screen.getAllByRole('status')).toHaveLength(3)
  for (const label of ['A', 'B', 'C']) expect(screen.getByText(label)).toBeTruthy()
})
