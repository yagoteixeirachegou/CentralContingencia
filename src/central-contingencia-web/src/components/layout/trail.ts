import { matchPath } from 'react-router-dom'
import { httpFacebookSheet } from './sheets'

export interface Crumb { label: string; to?: string }

// Trilha de cada rota. O último item é a página atual; os anteriores levam de volta (mantendo os filtros do grid quando existem).
export function trailFor(pathname: string, gridPath: string): Crumb[] {
  const home: Crumb = { label: 'Início', to: '/' }
  const sheet: Crumb = { label: httpFacebookSheet.label, to: gridPath }
  const is = (pattern: string) => matchPath({ path: pattern, end: true }, pathname) !== null
  if (is('/')) return [{ label: 'Início' }]
  if (is('/dashboard')) return [home, { label: 'Dashboard' }]
  if (is('/tasks')) return [home, { label: 'Tarefas' }]
  if (is('/tasks/executors')) return [home, { label: 'Tarefas', to: '/tasks' }, { label: 'Executores' }]
  if (is('/proxies')) return [home, { label: httpFacebookSheet.label }, { label: 'Proxies' }]
  if (is('/proxies/import')) return [home, sheet, { label: 'Importar CSV' }]
  if (is('/proxies/ingestion')) return [home, sheet, { label: 'Perfis', to: '/profiles' }, { label: 'Novos perfis (TXT)' }]
  if (is('/profiles')) return [home, sheet, { label: 'Perfis' }]
  if (is('/profiles/:id')) return [home, sheet, { label: 'Perfis', to: '/profiles' }, { label: 'Detalhes do perfil' }]
  if (is('/warmup')) return [home, sheet, { label: 'Aquecimento' }]
  if (is('/administration')) return [home, { label: 'Administração' }]
  return [home]
}
