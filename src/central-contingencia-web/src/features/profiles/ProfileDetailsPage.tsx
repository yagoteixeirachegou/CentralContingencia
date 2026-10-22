import { useEffect, useRef, useState } from 'react'
import { ProxyCredentialsPanel } from '../proxies/ProxyCredentialsPanel'
import { AccessDenied } from '../../components/layout/StatusPages'
import { Link, useLocation, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../auth/auth-context'
import { getProxy } from '../proxies/api'
import { Button } from '../../components/ui/button'
import { Dialog } from '../../components/ui/dialog'
import { ApiError } from '../../lib/api/client'
import { confirmAllocation, currentAllocation, getProfile, operationalError, refreshOperations, releaseAllocation } from './api'
import { allocationLabels, dateLabel, profileLabels, profileRead, profileWrite } from './contracts'
import { AllocationHistory } from './AllocationHistory'
import { ReserveProxyForm } from './ReserveProxyForm'
import { PageSkeleton, ListSkeleton } from '../../components/ui/skeleton'
import { gridReturn } from '../proxies/grid/navigation'
export function ProfileDetailsPage() {
  const { id } = useParams()
  return id ? <ProfileDetails key={id} id={id} /> : <p role="alert">Perfil não encontrado.</p>
}
function ProfileDetails({ id }: { id: string }) {
  const returnTo = gridReturn(useLocation().state)
  const { user } = useAuth(); const client = useQueryClient(); const guard = useRef(false)
  const allowed = profileRead(user?.roles); const canWrite = profileWrite(user?.roles)
  const [reserve, setReserve] = useState(false); const [reserveBusy, setReserveBusy] = useState(false)
  const [releaseId, setReleaseId] = useState<string | null>(null)
  const [message, setMessage] = useState(''); const [error, setError] = useState('')
  const [now, setNow] = useState(Date.now)
  const profile = useQuery({ queryKey: ['profiles', 'detail', id], queryFn: ({ signal }) => getProfile(id, signal), enabled: allowed })
  const current = useQuery({ queryKey: ['allocations', 'current', id], queryFn: ({ signal }) => currentAllocation(id, signal), enabled: allowed && profile.isSuccess, refetchInterval: 15_000 })
  const allocation = current.data
  const proxy = useQuery({ queryKey: ['proxies', 'detail', allocation?.proxyId], queryFn: ({ signal }) => getProxy(allocation!.proxyId, signal), enabled: Boolean(allocation) && allowed })
  const deadline = allocation?.status === 'Reserved' ? allocation.reservationExpiresAt : null
  useEffect(() => {
    if (!deadline) return
    const update = () => setNow(Date.now())
    update()
    const timer = setTimeout(update, Math.max(0, Math.min(2147483647, Date.parse(deadline) - Date.now())))
    window.addEventListener('focus', update)
    return () => { clearTimeout(timer); window.removeEventListener('focus', update) }
  }, [deadline])
  const expired = Boolean(deadline && Date.parse(deadline) <= now)
  const occupied = Boolean(allocation && (allocation.status === 'Confirmed' || allocation.status === 'Reserved' && !expired))
  const mutation = useMutation({ mutationFn: ({ action, allocationId }: { action: 'confirm' | 'release'; allocationId: string }) => action === 'confirm' ? confirmAllocation(allocationId) : releaseAllocation(allocationId), retry: false })
  async function act(action: 'confirm' | 'release', allocationId: string) {
    if (guard.current || !canWrite) return
    guard.current = true; setError(''); setMessage('')
    try { await mutation.mutateAsync({ action, allocationId }); setReleaseId(null); setMessage(action === 'confirm' ? 'Reserva confirmada.' : 'Alocação liberada. A API reavaliará a disponibilidade da proxy.') }
    catch (reason) { setError(operationalError(reason)); setReleaseId(null) }
    finally { await refreshOperations(client); guard.current = false }
  }
  if (!allowed || profile.error instanceof ApiError && profile.error.status === 403) return <AccessDenied reason="Seu usuário não tem permissão para consultar este perfil." />
  if (profile.isPending) return <PageSkeleton label="Carregando perfil…" />
  if (profile.isError) return <section className="space-y-4"><Link to={returnTo} className="text-link underline">Voltar ao grid</Link><p role="alert" className="text-red-700">{operationalError(profile.error)}</p><Button variant="outline" onClick={() => void profile.refetch()}>Tentar novamente</Button></section>
  const details = profile.data
  return <section className="space-y-6">
    <header className="flex flex-wrap items-start justify-between gap-3"><div><Link to={returnTo} className="text-sm text-link underline">Voltar ao grid</Link><h1 className="mt-2 break-words text-3xl font-semibold">{details.name}</h1><p className="mt-2 text-sm text-slate-600">{details.platform1 ?? ''}{details.platform2 ? ` · ${details.platform2}` : ''} · {profileLabels[details.status]}</p></div><Button variant="outline" disabled={current.isFetching || profile.isFetching || mutation.isPending} onClick={() => { setError(''); void refreshOperations(client) }}>Atualizar dados</Button></header>
    <div className="rounded-2xl border border-slate-200 bg-surface p-5 text-sm"><p>Criado em {dateLabel(details.createdAt)}</p><p>Atualizado em {dateLabel(details.updatedAt)}</p>{details.closedAt && <p>Encerrado em {dateLabel(details.closedAt)}</p>}</div>
    {message && <p role="status" className="rounded-xl bg-emerald-50 p-3 text-sm text-emerald-800">{message}</p>}
    {error && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-red-700">{error}</p>}
    {!canWrite && <p className="text-sm text-slate-600">Modo de consulta. Reservar, confirmar e liberar exige Admin ou Manager.</p>}
    <section className="rounded-2xl border border-slate-200 bg-surface p-5" aria-labelledby="current-allocation-title"><h2 id="current-allocation-title" className="text-xl font-semibold">Proxy vinculada / reserva</h2>
      {current.isPending ? <ListSkeleton label="Carregando alocação atual…" rows={2} className="mt-3" /> : current.isError ? <div className="mt-3"><p role="alert" className="text-red-700">{operationalError(current.error)}</p><Button variant="outline" onClick={() => void current.refetch()}>Tentar novamente</Button></div> : allocation ? <div className="mt-4 space-y-3">
        <p className="text-sm font-medium">{expired ? 'Reserva com prazo vencido' : allocationLabels[allocation.status]}</p>
        {proxy.isPending ? <ListSkeleton label="Carregando proxy…" rows={2} /> : proxy.isError ? <p role="alert" className="text-red-700">{operationalError(proxy.error)}</p> : proxy.data && <div className="space-y-2"><p className="break-words text-sm">{proxy.data.host}:{proxy.data.port} · {proxy.data.region}</p><ProxyCredentialsPanel proxyId={proxy.data.id} host={proxy.data.host} port={proxy.data.port} showEndpoint={false} /></div>}
        {allocation.status === 'Reserved' && <p className="text-sm">Prazo da reserva (horário local): <time dateTime={allocation.reservationExpiresAt}>{dateLabel(allocation.reservationExpiresAt)}</time></p>}
        {expired && <p className="text-sm text-amber-800">O prazo informado venceu segundo o relógio local. A API revalida a expiração e recupera a reserva na próxima operação. Nenhuma confirmação será enviada automaticamente.</p>}
        {canWrite && occupied && <div className="flex flex-wrap gap-2">{allocation.status === 'Reserved' && <Button disabled={mutation.isPending || current.isFetching} onClick={() => void act('confirm', allocation.id)}>Confirmar reserva</Button>}<Button variant="outline" disabled={mutation.isPending || current.isFetching} onClick={() => { setError(''); setReleaseId(allocation.id) }}>Liberar proxy</Button></div>}
      </div> : <p className="mt-3 text-sm text-slate-600">Nenhuma proxy vinculada ou reserva atual.</p>}
      {canWrite && details.status === 'Active' && current.isSuccess && !occupied && <Button className="mt-4" disabled={current.isFetching || mutation.isPending} onClick={() => { setMessage(''); setReserve(true) }}>Reservar proxy</Button>}
      {details.status === 'Closed' && <p className="mt-3 text-sm text-slate-600">Perfil encerrado. O histórico permanece disponível.</p>}
    </section>
    <AllocationHistory profileId={id} />
    <Dialog open={reserve} onOpenChange={setReserve} title="Reservar proxy disponível" description="A seleção não garante a reserva; a API controla exclusividade e prazo." busy={reserveBusy}>{reserve && <ReserveProxyForm profileId={id} onBusy={setReserveBusy} onCancel={() => setReserve(false)} onReserved={() => { setReserve(false); setMessage('Proxy reservada. Confira o prazo antes de confirmar.') }} />}</Dialog>
    <Dialog open={Boolean(releaseId)} onOpenChange={open => { if (!open) setReleaseId(null) }} title="Liberar proxy?" description="Esta ação encerra o vínculo ou cancela a reserva, preservando o histórico. A proxy só poderá ser reutilizada se a API considerá-la disponível." busy={mutation.isPending}><div className="mt-6 flex justify-end gap-3"><Button variant="outline" disabled={mutation.isPending} onClick={() => setReleaseId(null)}>Cancelar</Button><Button disabled={mutation.isPending} onClick={() => { if (releaseId) void act('release', releaseId) }}>{mutation.isPending ? 'Liberando…' : 'Confirmar liberação'}</Button></div></Dialog>
  </section>
}
