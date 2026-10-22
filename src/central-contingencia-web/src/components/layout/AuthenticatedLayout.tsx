import { useEffect, useState } from 'react'
import { brand } from '../../lib/brand'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { Activity, ChartColumn, ListChecks, ChevronDown, House, Menu, Flame, Server, Users, PanelLeftClose, PanelLeftOpen, Shield, LogOut, Table2, X } from 'lucide-react'
import { useAuth } from '../../features/auth/auth-context'
import { profileRead } from '../../features/profiles/contracts'
import { Badge } from '../ui/badge'
import { Button } from '../ui/button'
import { ThemeToggle } from '../ui/theme-toggle'
import { Breadcrumbs } from './Breadcrumbs'
import { sheets, type Sheet } from './sheets'
const childIcons: Record<string, typeof Server> = { proxies: Server, perfis: Users, aquecimento: Flame }
// Aba com submenus: no menu expandido mostra o nome da aba e os itens recuados; recolhido (só ícones) mostra os itens soltos.
function SheetGroup({ sheet, collapsed, pathname, linkClass }: { sheet: Sheet; collapsed: boolean; pathname: string; linkClass(active: boolean): string }) {
  const children = sheet.children ?? []
  const inside = children.some(child => child.active?.(pathname))
  const [open, setOpen] = useState(true)
  // Navegar para dentro do grupo sempre o reabre.
  useEffect(() => { if (inside) setOpen(true) }, [inside])
  const listId = `submenu-${sheet.id}`
  return <div>
    <button type="button" aria-expanded={open} aria-controls={listId} onClick={() => setOpen(value => !value)}
      className={`flex w-full items-center gap-2 rounded-lg px-3 py-2.5 text-left text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-sky-700 ${inside ? 'text-link' : 'text-slate-600'} hover:bg-slate-100 ${collapsed ? 'lg:hidden' : ''}`}>
      <Table2 size={17} className="shrink-0" /><span className="min-w-0 flex-1">{sheet.label}</span>
      <ChevronDown size={16} aria-hidden="true" className={`shrink-0 transition-transform ${open ? '' : '-rotate-90'}`} /></button>
    <div id={listId} className={`mt-1 flex-col gap-1 ${collapsed ? '' : 'pl-4'} ${open ? 'flex' : collapsed ? 'hidden lg:flex' : 'hidden'}`}>
      {children.map(child => {
        const Icon = childIcons[child.id] ?? Table2; const on = child.active?.(pathname) ?? false
        if (!child.to) return <span key={child.id} aria-disabled="true" title={`${child.label} (em breve)`} className={`flex cursor-not-allowed items-center gap-2 rounded-lg px-3 py-2.5 text-sm font-medium text-slate-500 ${collapsed ? 'lg:justify-center lg:px-0' : ''}`}>
          <Icon size={17} className="shrink-0" /><span className={`min-w-0 ${collapsed ? 'lg:sr-only' : ''}`}>{child.label}<span className="mt-1 block w-fit whitespace-nowrap rounded bg-slate-100 px-1.5 py-0.5 text-[11px] uppercase text-slate-600">Em breve</span></span></span>
        return <Link key={child.id} to={child.to} aria-current={on ? 'page' : undefined} title={collapsed ? `${sheet.label} · ${child.label}` : undefined}
          className={linkClass(on) + (collapsed ? ' lg:justify-center lg:px-0' : '')}>
          <Icon size={17} className="shrink-0" /><span className={collapsed ? 'lg:sr-only' : ''}>{child.label}</span></Link>
      })}
    </div>
  </div>
}
export function AuthenticatedLayout() {
  const { user, logout } = useAuth()
  const [error, setError] = useState('')
  const [pending, setPending] = useState(false)
  const canRead = profileRead(user?.roles)
  const [menuOpen, setMenuOpen] = useState(false)
  const { pathname } = useLocation()
  // No computador o menu pode ficar só com os ícones; a escolha fica guardada neste navegador.
  const [collapsed, setCollapsed] = useState(() => { try { return localStorage.getItem('central-menu') === 'collapsed' } catch { return false } })
  const toggleCollapsed = () => setCollapsed(current => { const next = !current; try { localStorage.setItem('central-menu', next ? 'collapsed' : 'expanded') } catch { /* sem armazenamento: vale só nesta sessão */ } return next })
  const text = collapsed ? 'lg:sr-only' : ''
  // No celular o menu fica recolhido e fecha ao navegar.
  useEffect(() => setMenuOpen(false), [pathname])
  async function signOut() {
    setPending(true); setError('')
    try { await logout() } catch (reason) { setError(reason instanceof Error ? reason.message : 'Falha ao sair.') }
    finally { setPending(false) }
  }
  const navigation = ({ isActive }: { isActive: boolean }) => `flex items-center gap-2 rounded-lg px-3 py-2.5 text-sm font-medium transition-colors ${collapsed ? 'lg:justify-center lg:px-0' : ''} ${isActive ? 'bg-sky-800 text-white' : 'text-slate-600 hover:bg-slate-100'}`
  return <div className={`min-h-dvh bg-slate-50 lg:grid ${collapsed ? 'lg:grid-cols-[72px_1fr]' : 'lg:grid-cols-[240px_1fr]'}`}>
    <aside className={`border-b border-slate-200 bg-surface p-5 ${collapsed ? 'lg:p-3' : ''} lg:sticky lg:top-0 lg:flex lg:h-dvh lg:flex-col lg:overflow-y-auto lg:border-r lg:border-b-0`}>
      <div className={`flex items-center justify-between gap-3 ${collapsed ? 'lg:justify-center' : ''}`}><div className={`flex items-center gap-3 ${collapsed ? 'lg:hidden' : ''}`}><span className="rounded-xl bg-sky-800 p-2.5 text-white"><Activity size={22} /></span><p className="text-sm font-semibold leading-5 text-slate-950">{brand.first}<br />{brand.second}</p></div>
        <Button variant="outline" className="px-2.5 lg:hidden" aria-expanded={menuOpen} aria-controls="menu-principal" aria-label={menuOpen ? 'Fechar menu' : 'Abrir menu'} onClick={() => setMenuOpen(open => !open)}>{menuOpen ? <X size={20} aria-hidden="true" /> : <Menu size={20} aria-hidden="true" />}</Button>
        <Button variant="outline" className="hidden px-2.5 lg:inline-flex" aria-expanded={!collapsed} aria-controls="menu-principal" aria-label={collapsed ? 'Expandir menu' : 'Recolher menu'} title={collapsed ? 'Expandir menu' : 'Recolher menu'} onClick={toggleCollapsed}>{collapsed ? <PanelLeftOpen size={20} aria-hidden="true" /> : <PanelLeftClose size={20} aria-hidden="true" />}</Button></div>
      <nav id="menu-principal" aria-label="Navegação principal" className={`mt-6 flex-col gap-2 lg:flex ${menuOpen ? 'flex' : 'hidden'}`}>
        <NavLink to="/" end className={navigation} title={collapsed ? 'Início' : undefined}><House size={17} className="shrink-0" /><span className={text}>Início</span></NavLink>
        <NavLink to="/dashboard" className={navigation} title={collapsed ? 'Dashboard' : undefined}><ChartColumn size={17} className="shrink-0" /><span className={text}>Dashboard</span></NavLink>
        {canRead && <NavLink to="/tasks" className={navigation} title={collapsed ? 'Tarefas' : undefined}><ListChecks size={17} className="shrink-0" /><span className={text}>Tarefas</span></NavLink>}
        <p className={`mt-3 px-3 text-xs font-semibold uppercase tracking-wide text-slate-500 ${collapsed ? 'lg:hidden' : ''}`}>Planilhas</p>
        {collapsed && <hr className="my-1 hidden border-slate-200 lg:block" />}
        {sheets.map(sheet => sheet.children ? (canRead && <SheetGroup key={sheet.id} sheet={sheet} collapsed={collapsed} pathname={pathname} linkClass={isActive => navigation({ isActive })} />) : sheet.to ? <NavLink key={sheet.id} to={sheet.to} className={navigation} title={collapsed ? sheet.label : undefined}><Table2 size={17} className="shrink-0" /><span className={text}>{sheet.label}</span></NavLink>
          : <span key={sheet.id} aria-disabled="true" title={collapsed ? `${sheet.label} (em breve)` : undefined} className={`flex cursor-not-allowed items-center gap-2 rounded-lg px-3 py-2.5 text-sm font-medium text-slate-500 ${collapsed ? 'lg:justify-center lg:px-0' : ''}`}><Table2 size={17} className="shrink-0" /><span className={`min-w-0 ${text}`}>{sheet.label}<span className="mt-1 block w-fit whitespace-nowrap rounded bg-slate-100 px-1.5 py-0.5 text-[11px] uppercase text-slate-600">Em breve</span></span></span>)}
        {user?.roles.includes('Admin') && <NavLink to="/administration" className={navigation} title={collapsed ? 'Administração' : undefined}><Shield size={17} className="shrink-0" /><span className={text}>Administração</span></NavLink>}
      </nav>
      <div className={`mt-6 lg:mt-auto lg:pt-6 ${collapsed ? 'lg:hidden' : 'lg:block'} ${menuOpen ? 'block' : 'hidden'}`}><Badge>DESENVOLVIMENTO</Badge><p className="mt-2 text-xs text-slate-500">Ambiente local de homologação</p></div>
    </aside>
    <div className="min-w-0">
      <header className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 bg-surface px-5 py-4 lg:px-8">
        <Breadcrumbs /><div className="flex items-center gap-4"><div className="text-right"><p className="max-w-[200px] truncate text-sm font-medium text-slate-800">{user?.email}</p><p className="text-xs text-slate-500">{user?.roles.join(' · ')}</p></div><ThemeToggle /><Button variant="outline" className="gap-2 text-sm" onClick={signOut} disabled={pending}><LogOut size={15} />{pending ? 'Saindo…' : 'Sair'}</Button></div>
      </header>
      {error && <p role="alert" className="m-5 text-sm text-red-700">{error}</p>}
      <main className="mx-auto max-w-[1500px] p-4 sm:p-6 lg:p-8"><Outlet /></main>
    </div>
  </div>
}
