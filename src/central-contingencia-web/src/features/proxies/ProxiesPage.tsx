import { useCallback, useEffect, useRef, useState } from 'react'
import { AccessDenied } from '../../components/layout/StatusPages'
import { Link, useLocation, useSearchParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../auth/auth-context'
import { ApiError } from '../../lib/api/client'
import { Button } from '../../components/ui/button'
import { Dialog } from '../../components/ui/dialog'
import { getProxy, proxyError } from './api'
import { statusLabels } from './contracts'
import { ProxyForm } from './ProxyForm'
import { ProxyCredentialsPanel } from './ProxyCredentialsPanel'
import { profileRead, profileWrite, dateLabel } from '../profiles/contracts'
import { confirmAllocation, operationalError, refreshOperations, releaseAllocation } from '../profiles/api'
import { OperationalPagination } from '../profiles/OperationalPagination'
import { listOperationalGrid } from './grid/api'
import { assignments, availabilityLabels, type GridOptions, type GridRow, type Assignment, type Availability } from './grid/contracts'
import { GridFilters } from './grid/GridFilters'
import { OperationalGridTable } from './grid/OperationalGridTable'
import { httpFacebookSheet } from '../../components/layout/sheets'
import { LinkProfileForm } from './grid/LinkProfileForm'
import { TableSkeleton } from '../../components/ui/skeleton'
import { FileUp, Plus, RefreshCw } from 'lucide-react'
import { AssignmentsForm } from './grid/AssignmentsForm'
export function ProxiesPage() {
  const { user } = useAuth(); const client = useQueryClient(); const location = useLocation()
  const allowed = profileRead(user?.roles); const canWrite = profileWrite(user?.roles)
  const [params, setParams] = useSearchParams()
  const latestParams = useRef(params)
  useEffect(() => { latestParams.current = params }, [params])
  const [filterReset, setFilterReset] = useState(0)
  const pageValue = Number(params.get('page') ?? 1); const sizeValue = Number(params.get('pageSize') ?? 20)
  const options: GridOptions = { page: Number.isInteger(pageValue) && pageValue >= 1 && pageValue <= 10000 ? pageValue : 1,
    pageSize: [20, 50, 100].includes(sizeValue) ? sizeValue : 20, sort: 'expiresAt', direction: params.get('direction') === 'desc' ? 'desc' : 'asc',
    profile: params.get('profile')?.trim() || undefined, region: params.get('region')?.trim() || undefined,
    platform: assignments.includes(params.get('platform') as Assignment) ? params.get('platform') as Assignment : undefined,
    availability: Object.hasOwn(availabilityLabels, params.get('availability') ?? '') ? params.get('availability') as Availability : undefined }
  const setOption = useCallback((name: string, value: string) => {
    const next = new URLSearchParams(latestParams.current); if (value) next.set(name, value); else next.delete(name)
    if (name !== 'page') next.set('page', '1')
    latestParams.current = next; setParams(next, { replace: true })
  }, [setParams])
  const setProfile = useCallback((value: string) => setOption('profile', value), [setOption])
  const setRegion = useCallback((value: string) => setOption('region', value), [setOption])
  const clearFilters = () => { latestParams.current = new URLSearchParams(); setParams({}, { replace: true }); setFilterReset(value => value + 1) }
  const filtered = Boolean(options.profile || options.region || options.platform || options.availability)
  const [editor, setEditor] = useState<string | null>(null); const [detailsId, setDetailsId] = useState<string | null>(null)
  const [link, setLink] = useState<GridRow | null>(null); const [assign, setAssign] = useState<GridRow | null>(null); const [release, setRelease] = useState<GridRow | null>(null)
  const [busy, setBusy] = useState(false); const [success, setSuccess] = useState(''); const [actionError, setActionError] = useState('')
  const actionGuard = useRef(false); const [acting, setActing] = useState(false)
  // Single attempt per click: no automatic retries; the grid is refreshed after success or failure.
  const act = useCallback(async (action: 'confirm' | 'release', row: GridRow) => {
    if (actionGuard.current || !row.allocationId) return
    actionGuard.current = true; setActing(true); setActionError(''); setSuccess('')
    try {
      if (action === 'confirm') await confirmAllocation(row.allocationId); else await releaseAllocation(row.allocationId)
      setSuccess(action === 'confirm' ? `Reserva de ${row.host}:${row.port} confirmada para ${row.profileName}.` : `${row.host}:${row.port} liberada de ${row.profileName}.`)
      setRelease(null)
    } catch (reason) { setActionError(operationalError(reason)) }
    finally { await refreshOperations(client); actionGuard.current = false; setActing(false) }
  }, [client])
  const onConfirm = useCallback((row: GridRow) => void act('confirm', row), [act])
  const onLink = useCallback((row: GridRow) => { setSuccess(''); setLink(row) }, [])
  const onAssignments = useCallback((row: GridRow) => { setSuccess(''); setAssign(row) }, [])
  const onRelease = useCallback((row: GridRow) => { setSuccess(''); setActionError(''); setRelease(row) }, [])
  const returnTo = location.pathname + location.search
  const list = useQuery({ queryKey: ['proxies', 'operational-grid', options], queryFn: ({ signal }) => listOperationalGrid(options, signal), enabled: allowed, refetchInterval: 30_000 })
  const detailId = editor && editor !== 'new' ? editor : detailsId
  const details = useQuery({ queryKey: ['proxies', 'detail', detailId], queryFn: ({ signal }) => getProxy(detailId!, signal), enabled: allowed && Boolean(detailId), staleTime: 0, refetchOnWindowFocus: false })
  useEffect(() => {
    const deadlines = list.data?.items.map(row => row.reservationExpiresAt ? Date.parse(row.reservationExpiresAt) : Infinity).filter(deadline => deadline > Date.now() && Number.isFinite(deadline)) ?? []
    if (!deadlines.length) return
    // A deadline requests fresh server data, never a local availability transition.
    const timer = setTimeout(() => void client.invalidateQueries({ queryKey: ['proxies', 'operational-grid'] }), Math.min(2147483647, Math.max(0, Math.min(...deadlines) - Date.now()) + 100))
    return () => clearTimeout(timer)
  }, [list.data, client])
  if (!allowed || list.error instanceof ApiError && list.error.status === 403) return <AccessDenied reason="Seu papel não permite consultar o grid operacional." />
  return <section className="space-y-4">
    <header className="flex flex-wrap items-start justify-between gap-3"><div><h1 className="text-3xl font-semibold text-slate-950">Proxies</h1><p className="mt-1 text-sm text-slate-600">{httpFacebookSheet.label} · Uma linha por proxy. Disponibilidade informada pela API.</p></div><div className="flex flex-wrap gap-2">
      <Button variant="outline" className="gap-2" disabled={list.isFetching} onClick={() => void client.invalidateQueries({ queryKey: ['proxies'] })}><RefreshCw size={16} aria-hidden="true" />Atualizar</Button>
      {user?.roles.includes('Admin') && <Link to="/proxies/import" state={{ returnTo }} className="inline-flex items-center gap-2 rounded-md border border-slate-300 bg-surface px-3 py-2 text-sm text-link"><FileUp size={16} aria-hidden="true" />Importar CSV</Link>}
      {canWrite && <Button className="gap-2" onClick={() => { setSuccess(''); setEditor('new') }}><Plus size={16} aria-hidden="true" />Novo proxy</Button>}
    </div></header>
    {actionError && !release && <p role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-800">{actionError}</p>}
    {success && <p role="status" className="rounded-lg bg-emerald-50 p-3 text-sm text-emerald-800">{success}</p>}
    {!canWrite && <p className="text-sm text-slate-600">Modo de consulta. Operações são exclusivas de Admin e Manager.</p>}
    <div className="overflow-hidden rounded-2xl border border-slate-200 bg-surface shadow-sm">
      <GridFilters key={filterReset} options={options} onProfile={setProfile} onRegion={setRegion} onOption={setOption} onClear={clearFilters} />
      {list.isPending ? <TableSkeleton label="Carregando proxies…" columns={7} rows={8} /> : list.isError ? <div className="p-6"><p role="alert" className="mb-3 text-red-700">{operationalError(list.error)}</p><Button variant="outline" onClick={() => void list.refetch()}>Tentar novamente</Button></div> : list.data?.items.length ?
        <OperationalGridTable items={list.data.items} options={options} total={list.data.total} evaluatedAt={list.data.evaluatedAt} busy={list.isFetching} canWrite={canWrite} actionBusy={acting} returnTo={returnTo} onEdit={setEditor} onDetails={setDetailsId} onLink={onLink} onAssignments={onAssignments} onConfirm={onConfirm} onRelease={onRelease} /> : <div className="p-8"><h2 className="font-semibold">Nenhum proxy encontrado</h2><p className="mt-2 text-sm text-slate-500">{filtered ? 'Nenhuma conexão corresponde aos filtros aplicados.' : 'Cadastre uma conexão para começar.'}</p>{filtered && <Button variant="outline" className="mt-4" onClick={clearFilters}>Limpar filtros</Button>}</div>}
      {list.data && !list.isError && <OperationalPagination label="proxies" page={options.page} pageSize={options.pageSize} total={list.data.total} busy={list.isFetching} onPage={page => setOption('page', String(page))} onSize={size => setOption('pageSize', String(size))} />}
    </div>
    {list.isFetching && !list.isPending && <p role="status" className="text-xs text-slate-500">Atualizando dados…</p>}
    <Dialog open={editor !== null || detailsId !== null} busy={busy} onOpenChange={open => { if (!open) { setEditor(null); setDetailsId(null) } }} title={editor === 'new' ? 'Cadastrar proxy' : editor ? 'Editar proxy' : 'Detalhes da proxy'} description="Dados operacionais. Credenciais persistidas nunca são recuperadas.">
      {editor === 'new' ? <ProxyForm onBusy={setBusy} onCancel={() => setEditor(null)} onSaved={() => { setSuccess('Proxy cadastrado com sucesso.'); setEditor(null) }} /> : details.isError ? <div className="mt-4"><p role="alert" className="text-red-700">{proxyError(details.error)}</p><Button variant="outline" onClick={() => void details.refetch()}>Tentar novamente</Button></div> : !details.data || details.isFetching ? <p role="status" className="py-5">Carregando detalhes…</p> : editor ? <ProxyForm key={editor} original={details.data} onBusy={setBusy} onCancel={() => setEditor(null)} onSaved={() => { setSuccess('Alterações salvas com sucesso.'); setEditor(null) }} /> : <><dl className="mt-4 space-y-2 text-sm"><div><dt>Proxy</dt><dd>{details.data.host}:{details.data.port}</dd></div><div><dt>Região</dt><dd>{details.data.region}</dd></div><div><dt>Status operacional</dt><dd>{statusLabels[details.data.status]}</dd></div><div><dt>Vencimento</dt><dd>{dateLabel(details.data.expiresAt)}</dd></div></dl><div className="mt-4"><ProxyCredentialsPanel proxyId={details.data.id} host={details.data.host} port={details.data.port} /></div></>}
    </Dialog>
    <Dialog open={link !== null} busy={busy} onOpenChange={open => { if (!open) setLink(null) }} title="Vincular perfil à proxy" description="Cria o perfil quando necessário, reserva e confirma o vínculo.">{link && <LinkProfileForm row={link} onBusy={setBusy} onCancel={() => setLink(null)} onLinked={message => { setLink(null); setSuccess(message) }} />}</Dialog>
    <Dialog open={assign !== null} busy={busy} onOpenChange={open => { if (!open) setAssign(null) }} title={`Plataformas de ${assign?.profileName ?? 'perfil'}`} description="Classificações do perfil. Não alteram a disponibilidade da proxy.">{assign && <AssignmentsForm row={assign} onBusy={setBusy} onCancel={() => setAssign(null)} onSaved={message => { setAssign(null); setSuccess(message) }} />}</Dialog>
    <Dialog open={release !== null} busy={acting} onOpenChange={open => { if (!open) { setRelease(null); setActionError('') } }} title="Liberar proxy?" description="Encerra o vínculo ou cancela a reserva, preservando o histórico. O perfil continua ativo, sem proxy. A proxy só volta a ficar disponível se a API considerá-la operacional e válida.">{release && <div className="mt-4 space-y-4"><p className="text-sm"><span className="font-medium">{release.profileName}</span> — {release.host}:{release.port}</p>{actionError && <p role="alert" className="text-sm text-red-700">{actionError}</p>}<div className="flex justify-end gap-3"><Button variant="outline" disabled={acting} onClick={() => { setRelease(null); setActionError('') }}>Cancelar</Button><Button disabled={acting} onClick={() => void act('release', release)}>{acting ? 'Liberando…' : 'Confirmar liberação'}</Button></div></div>}</Dialog>
  </section>
}
