import { useState } from 'react'
import { UserPlus } from 'lucide-react'
import { AccessDenied } from '../../components/layout/StatusPages'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { flexRender, getCoreRowModel, useReactTable, type ColumnDef } from '@tanstack/react-table'
import { useAuth } from '../auth/auth-context'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { Dialog } from '../../components/ui/dialog'
import { ApiError } from '../../lib/api/client'
import { listProfiles, operationalError } from './api'
import { dateLabel, profileLabels, profileRead, profileWrite, type OperationalProfile, type ProfileOptions, type ProfileSort, type ProfileStatus } from './contracts'
import { OperationalPagination } from './OperationalPagination'
import { ProfileForm } from './ProfileForm'
import { TableSkeleton } from '../../components/ui/skeleton'
import { gridReturn } from '../proxies/grid/navigation'
const initial: ProfileOptions = { page: 1, pageSize: 20, sort: 'createdAt', direction: 'desc' }
const columns = (returnTo: string): ColumnDef<OperationalProfile>[] => [
  { accessorKey: 'name', header: 'Nome', cell: ({ row }) => <Link className="font-medium text-link underline" to={`/profiles/${row.original.id}`} state={{ returnTo }}>{row.original.name}</Link> },
  { accessorKey: 'platform', header: 'Plataforma' },
  { accessorKey: 'status', header: 'Status', cell: ({ row }) => profileLabels[row.original.status] },
  { accessorKey: 'createdAt', header: 'Criação', cell: ({ row }) => dateLabel(row.original.createdAt) },
]
export function ProfilesPage() {
  const returnTo = gridReturn(useLocation().state)
  const { user } = useAuth(); const client = useQueryClient(); const navigate = useNavigate()
  const allowed = profileRead(user?.roles); const canWrite = profileWrite(user?.roles)
  const [options, setOptions] = useState(initial)
  const [filters, setFilters] = useState({ name: '', platform: '', status: '' })
  const [open, setOpen] = useState(false); const [busy, setBusy] = useState(false)
  const list = useQuery({ queryKey: ['profiles', 'list', options], queryFn: ({ signal }) => listProfiles(options, signal), enabled: allowed })
  const table = useReactTable({ data: list.data?.items ?? [], columns: columns(returnTo), getCoreRowModel: getCoreRowModel(), manualPagination: true, manualSorting: true, getRowId: row => row.id })
  if (!allowed || list.error instanceof ApiError && list.error.status === 403) return <AccessDenied reason="Seu usuário não tem permissão para consultar perfis." />
  return <section className="space-y-6">
    <Link to={returnTo} className="text-sm text-link underline">Voltar ao grid</Link>
    <header className="flex flex-wrap items-start justify-between gap-4"><div><h1 className="text-3xl font-semibold text-slate-950">Perfis</h1><p className="mt-2 text-sm text-slate-600">HTTP - FACEBOOK · Cadastre e consulte perfis, inclusive aqueles sem proxy. Vínculos e liberações ficam em Proxies.</p></div><div className="flex flex-wrap gap-2"><Button variant="outline" disabled={list.isFetching} onClick={() => void client.invalidateQueries({ queryKey: ['profiles'] })}>Atualizar</Button>{canWrite && <Link to="/proxies/ingestion" className="inline-flex items-center gap-2 rounded-md border border-slate-300 bg-surface px-3 py-2 text-sm text-link"><UserPlus size={16} aria-hidden="true" />Novos perfis (TXT)</Link>}{canWrite && <Button onClick={() => setOpen(true)}>Novo perfil</Button>}</div></header>
    {!canWrite && <p className="text-sm text-slate-600">Modo de consulta. Cadastro e alocações são exclusivos de Admin e Manager.</p>}
    <div className="overflow-hidden rounded-2xl border border-slate-200 bg-surface shadow-sm">
      <form className="space-y-4 p-5" onSubmit={event => { event.preventDefault(); setOptions(current => ({ ...current, page: 1, name: filters.name.trim() || undefined, platform: filters.platform.trim() || undefined, status: (filters.status || undefined) as ProfileStatus | undefined })) }}>
        <div className="grid gap-3 sm:grid-cols-3">{(['name', 'platform'] as const).map(name => <div key={name}><label htmlFor={`profile-filter-${name}`} className="mb-1.5 block text-sm">{name === 'name' ? 'Nome exato' : 'Plataforma exata'}</label><Input id={`profile-filter-${name}`} maxLength={name === 'name' ? 200 : 100} value={filters[name]} onChange={event => setFilters(current => ({ ...current, [name]: event.target.value }))} /></div>)}<div><label htmlFor="profile-filter-status" className="mb-1.5 block text-sm">Status do perfil</label><Select id="profile-filter-status" value={filters.status} onChange={event => setFilters(current => ({ ...current, status: event.target.value }))}><option value="">Todos</option>{Object.entries(profileLabels).map(([value, text]) => <option key={value} value={value}>{text}</option>)}</Select></div></div>
        <div className="flex flex-wrap items-center justify-between gap-3"><p className="text-xs text-slate-500">A pesquisa utiliza correspondência exata, conforme a API.</p><div className="flex gap-2"><Button type="button" variant="outline" onClick={() => { setFilters({ name: '', platform: '', status: '' }); setOptions(initial) }}>Limpar</Button><Button>Aplicar filtros</Button></div></div>
      </form>
      <div className="flex flex-wrap items-center gap-3 border-t border-slate-100 p-4"><label htmlFor="profile-sort" className="text-sm">Ordenar por</label><Select id="profile-sort" className="w-auto text-sm" value={options.sort} onChange={event => setOptions(current => ({ ...current, page: 1, sort: event.target.value as ProfileSort }))}>{Object.entries({ name: 'Nome', platform: 'Plataforma', status: 'Status', createdAt: 'Criação', updatedAt: 'Atualização' }).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</Select><Button variant="outline" onClick={() => setOptions(current => ({ ...current, page: 1, direction: current.direction === 'asc' ? 'desc' : 'asc' }))}>{options.direction === 'asc' ? 'Crescente' : 'Decrescente'}</Button></div>
      {list.isPending ? <TableSkeleton label="Carregando perfis…" columns={5} rows={6} /> : list.isError ? <div className="p-6"><p role="alert" className="mb-3 text-red-700">{operationalError(list.error)}</p><Button variant="outline" onClick={() => void list.refetch()}>Tentar novamente</Button></div> : list.data?.items.length ? <div className="overflow-x-auto" role="region" aria-label="Tabela de perfis" tabIndex={0}><table className="w-full min-w-[600px] text-left text-sm" aria-busy={list.isFetching}><thead className="border-y border-slate-200 bg-slate-50 text-slate-600">{table.getHeaderGroups().map(group => <tr key={group.id}>{group.headers.map(header => <th key={header.id} scope="col" className="px-5 py-3">{flexRender(header.column.columnDef.header, header.getContext())}</th>)}</tr>)}</thead><tbody className="divide-y divide-slate-100">{table.getRowModel().rows.map(row => <tr key={row.id}>{row.getVisibleCells().map(cell => <td key={cell.id} className="px-5 py-4">{flexRender(cell.column.columnDef.cell, cell.getContext())}</td>)}</tr>)}</tbody></table></div> : <p className="p-6">Nenhum perfil encontrado.</p>}
      {list.data && !list.isError && <OperationalPagination label="perfis" page={options.page} pageSize={options.pageSize} total={list.data.total} busy={list.isFetching} onPage={page => setOptions(current => ({ ...current, page }))} onSize={pageSize => setOptions(current => ({ ...current, page: 1, pageSize }))} />}
    </div>
    <Dialog open={open} onOpenChange={setOpen} title="Novo perfil operacional" description="Cadastre um perfil sem alterar ou criar uma proxy." busy={busy}>{open && <ProfileForm onBusy={setBusy} onCancel={() => setOpen(false)} onCreated={id => { setOpen(false); navigate(`/profiles/${id}`, { state: { returnTo } }) }} />}</Dialog>
  </section>
}
