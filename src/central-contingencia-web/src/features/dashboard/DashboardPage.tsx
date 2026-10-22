import { useMemo, useState } from 'react'
import { AccessDenied } from '../../components/layout/StatusPages'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { RefreshCw, Search } from 'lucide-react'
import { useAuth } from '../auth/auth-context'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { profileRead, profileWrite } from '../profiles/contracts'
import { dashboardError, getAdsPowerDashboard, refreshAdsPowerDashboard } from './api'
import { activeFilters, filterProfiles, groupSortLabels, noFilters, sortGroups, sortProfiles, type Direction, type GroupSort, type ProfileFilters, type ProfileSortKey } from './filters'
import { GroupTiles } from './GroupTiles'
import { CardsSkeleton, TableSkeleton } from '../../components/ui/skeleton'
import { ProfileTable } from './ProfileTable'
const number = new Intl.NumberFormat('pt-BR')
const time = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' })
const pageSize = 50
const toggle = (values: string[], value: string) => values.includes(value) ? values.filter(item => item !== value) : [...values, value]

function Figure({ label, value }: { label: string; value: number }) {
  return <div className="rounded-xl border border-slate-200 bg-surface px-4 py-3"><p className="text-sm text-slate-600">{label}</p><p className="text-2xl font-semibold tabular-nums text-slate-950">{number.format(value)}</p></div>
}
export function DashboardPage() {
  const { user } = useAuth(); const client = useQueryClient(); const allowed = profileRead(user?.roles); const canRefresh = profileWrite(user?.roles)
  const [refreshError, setRefreshError] = useState('')
  const [filters, setFilters] = useState<ProfileFilters>(noFilters); const [groupSort, setGroupSort] = useState<GroupSort>('count-desc')
  const [sort, setSort] = useState<{ key: ProfileSortKey; direction: Direction }>({ key: 'name', direction: 'asc' }); const [visible, setVisible] = useState(pageSize)
  const query = useQuery({ queryKey: ['adspower', 'dashboard'], queryFn: ({ signal }) => getAdsPowerDashboard(signal), enabled: allowed, staleTime: 60_000, retry: false, refetchOnWindowFocus: false })
  const refresh = useMutation({ mutationFn: refreshAdsPowerDashboard, retry: false,
    onSuccess: data => { setRefreshError(''); client.setQueryData(['adspower', 'dashboard'], data) }, onError: reason => setRefreshError(dashboardError(reason)) })
  const data = query.data
  const groups = useMemo(() => data ? sortGroups(data.groups, groupSort) : [], [data, groupSort])
  const matches = useMemo(() => data ? sortProfiles(filterProfiles(data.profiles, filters), sort.key, sort.direction) : [], [data, filters, sort])
  if (!allowed) return <AccessDenied reason="Seu papel não permite consultar o dashboard." />
  const busy = refresh.isPending || query.isFetching; const now = data ? new Date(data.generatedAt) : new Date(); const active = activeFilters(filters)
  const change = (next: ProfileFilters) => { setFilters(next); setVisible(pageSize) }
  const sortBy = (key: ProfileSortKey) => { setSort(current => ({ key, direction: current.key === key && current.direction === 'asc' ? 'desc' : 'asc' })); setVisible(pageSize) }
  return <section className="space-y-5">
    <header className="flex flex-wrap items-start justify-between gap-3"><div><h1 className="text-3xl font-semibold text-slate-950">Dashboard</h1><p className="mt-1 text-sm text-slate-600">Perfis do AdsPower por grupo e tag, lidos em modo somente leitura.</p></div>
      {canRefresh && <Button variant="outline" className="gap-2" disabled={busy} onClick={() => refresh.mutate()}><RefreshCw size={16} aria-hidden="true" />{refresh.isPending ? 'Lendo o AdsPower…' : 'Atualizar do AdsPower'}</Button>}
    </header>
    {refreshError && <p role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-800">{refreshError}</p>}
    {query.isPending ? <div className="space-y-4"><CardsSkeleton label="Lendo os perfis do AdsPower. Com muitos perfis, isso pode levar alguns segundos." cards={4} /><div className="rounded-2xl border border-slate-200 bg-surface"><TableSkeleton label="Montando a tabela de perfis…" columns={5} rows={8} /></div></div>
      : query.isError ? <div className="rounded-2xl border border-slate-200 bg-surface p-6"><p role="alert" className="mb-3 text-red-700">{dashboardError(query.error)}</p><Button variant="outline" onClick={() => void query.refetch()}>Tentar novamente</Button></div>
      : data && data.total === 0 && data.groups.length === 0 ? <div className="rounded-2xl border border-slate-200 bg-surface p-8"><h2 className="font-semibold">Nenhum perfil encontrado no AdsPower</h2><p className="mt-2 text-sm text-slate-500">O AdsPower respondeu, mas não há perfis cadastrados nele.</p></div>
      : data && <>
        <div className="grid grid-cols-3 gap-3"><Figure label="Perfis" value={data.total} /><Figure label="Grupos" value={data.groups.length} /><Figure label="Tags" value={data.tags.length} /></div>
        <div className="rounded-2xl border border-slate-200 bg-surface p-4">
          <div className="flex flex-wrap items-end justify-between gap-3"><h2 className="text-lg font-semibold text-slate-950">Grupos</h2>
            <div className="flex items-center gap-2"><label htmlFor="group-sort" className="text-sm text-slate-600">Ordenar por</label>
              <Select id="group-sort" className="w-auto" value={groupSort} onChange={event => setGroupSort(event.target.value as GroupSort)}>{(Object.keys(groupSortLabels) as GroupSort[]).map(key => <option key={key} value={key}>{groupSortLabels[key]}</option>)}</Select></div></div>
          <p className="mb-3 mt-1 text-sm text-slate-500">Clique para filtrar os perfis. A idade do grupo é a do perfil mais antigo dele, porque o AdsPower não informa a criação do grupo.</p>
          <GroupTiles groups={groups} total={data.total} selected={filters.groupIds} now={now} onToggle={id => change({ ...filters, groupIds: toggle(filters.groupIds, id) })} />
        </div>
        <div className="rounded-2xl border border-slate-200 bg-surface p-4">
          <h2 className="text-lg font-semibold text-slate-950">Tags</h2><p className="mb-3 mt-1 text-sm text-slate-500">Com várias tags selecionadas, aparecem só os perfis que têm todas.</p>
          <ul className="flex flex-wrap gap-1.5">{data.tags.map(tag => { const on = filters.tags.includes(tag.name); return <li key={tag.name}>
            <button type="button" aria-pressed={on} aria-label={`${tag.name}: ${tag.total} ${tag.total === 1 ? 'perfil' : 'perfis'}`} onClick={() => change({ ...filters, tags: toggle(filters.tags, tag.name) })}
              className={`rounded-full border px-2.5 py-1 text-xs transition-colors focus-visible:outline-2 focus-visible:outline-sky-700 ${on ? 'border-sky-800 bg-sky-50 text-link' : 'border-slate-200 bg-surface text-slate-700 hover:bg-slate-50'}`}>
              <span aria-hidden="true">{tag.name} <span className="tabular-nums text-slate-500">{tag.total}</span></span></button></li> })}</ul>
        </div>
        <div className="overflow-hidden rounded-2xl border border-slate-200 bg-surface">
          <div className="flex flex-wrap items-end gap-3 p-4">
            <div className="min-w-[220px] flex-1"><label htmlFor="profile-search" className="mb-1 block text-sm font-medium text-slate-700">Buscar perfil pelo nome</label>
              <div className="relative"><Search size={16} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" aria-hidden="true" /><Input id="profile-search" className="pl-9" value={filters.search} maxLength={200} onChange={event => change({ ...filters, search: event.target.value })} /></div></div>
            <Button variant="outline" disabled={active === 0} onClick={() => change(noFilters)}>Limpar filtros{active > 0 ? ` (${active})` : ''}</Button>
          </div>
          <p role="status" className="px-4 pb-3 text-sm text-slate-600">{number.format(matches.length)} de {number.format(data.total)} perfis</p>
          {matches.length === 0 ? <div className="border-t border-slate-100 p-8 text-center"><h2 className="font-semibold">Nenhum perfil com esses filtros</h2><p className="mt-1 text-sm text-slate-500">Tire algum filtro para ver mais perfis.</p></div>
            : <ProfileTable profiles={matches.slice(0, visible)} sort={sort.key} direction={sort.direction} now={now} onSort={sortBy} onTag={tag => change({ ...filters, tags: filters.tags.includes(tag) ? filters.tags : [...filters.tags, tag] })} />}
          {matches.length > visible && <div className="border-t border-slate-100 p-3 text-center"><Button variant="outline" onClick={() => setVisible(count => count + pageSize)}>Mostrar mais {Math.min(pageSize, matches.length - visible)}</Button></div>}
        </div>
        <p className="text-xs text-slate-500">Leitura de {time.format(new Date(data.generatedAt))}{data.fromCache ? ' (resultado guardado por alguns minutos)' : ''}. Credenciais, senhas e cookies dos perfis não são guardados nem exibidos.</p>
      </>}
  </section>
}
