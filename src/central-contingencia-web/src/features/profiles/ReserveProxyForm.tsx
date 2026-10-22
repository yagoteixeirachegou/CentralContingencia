import { useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { listProxies } from '../proxies/api'
import type { ProxyListOptions } from '../proxies/contracts'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { OperationalPagination } from './OperationalPagination'
import { operationalError, refreshOperations, reserveProxy } from './api'
import { ListSkeleton } from '../../components/ui/skeleton'
import { dateLabel } from './contracts'
export function ReserveProxyForm({ profileId, onReserved, onCancel, onBusy }: { profileId: string; onReserved(): void; onCancel(): void; onBusy(value: boolean): void }) {
  const client = useQueryClient(); const guard = useRef(false); const key = useRef<string | null>(null)
  const [options, setOptions] = useState<ProxyListOptions>({ page: 1, pageSize: 20, sort: 'expiresAt', direction: 'asc', availableOnly: true })
  const [host, setHost] = useState(''); const [selected, setSelected] = useState(''); const [error, setError] = useState('')
  const list = useQuery({ queryKey: ['proxies', 'available-for-profile', options], queryFn: ({ signal }) => listProxies(options, signal) })
  const mutation = useMutation({ mutationFn: () => {
    key.current ??= crypto.randomUUID()
    return reserveProxy(profileId, selected, key.current)
  }, retry: false })
  const busy = mutation.isPending
  async function submit() {
    if (guard.current || !selected || list.isFetching || list.isError) return
    guard.current = true; setError(''); onBusy(true)
    try { await mutation.mutateAsync(); await refreshOperations(client); onReserved() }
    catch (reason) { setError(operationalError(reason)); await refreshOperations(client); setSelected('') }
    finally { guard.current = false; onBusy(false) }
  }
  return <div className="mt-5 space-y-4">
    <p className="text-sm text-slate-600">Selecione uma proxy informada como disponível pela API. O prazo é definido pelo backend. A disponibilidade será revalidada ao reservar.</p>
    <form className="flex flex-wrap items-end gap-2" onSubmit={event => { event.preventDefault(); setSelected(''); setOptions(current => ({ ...current, page: 1, host: host.trim() || undefined })) }}><div className="min-w-0 flex-1"><label htmlFor="reserve-host" className="mb-1 block text-sm">Host exato</label><Input id="reserve-host" maxLength={253} disabled={busy} value={host} onChange={event => setHost(event.target.value)} /></div><Button variant="outline" disabled={busy}>Pesquisar disponíveis</Button></form>
    {list.isPending ? <ListSkeleton label="Carregando proxies disponíveis…" rows={3} /> : list.isError ? <div><p role="alert" className="text-sm text-red-700">{operationalError(list.error)}</p><Button variant="outline" disabled={busy} onClick={() => void list.refetch()}>Tentar novamente</Button></div> : list.data?.items.length ? <fieldset disabled={busy || list.isFetching} className="space-y-2"><legend className="mb-2 text-sm font-medium">Proxies disponíveis</legend>{list.data.items.map(proxy => <label key={proxy.id} className="flex cursor-pointer items-start gap-3 rounded-xl border border-slate-200 p-3 text-sm"><input type="radio" name="available-proxy" value={proxy.id} checked={selected === proxy.id} disabled={!proxy.available} onChange={() => setSelected(proxy.id)} aria-label={`Selecionar ${proxy.host}:${proxy.port}`} /><span className="min-w-0 break-words"><span className="font-medium">{proxy.host}:{proxy.port}</span><span className="mt-1 block text-xs text-slate-600">{proxy.region} · Vence em {dateLabel(proxy.expiresAt)}</span></span></label>)}</fieldset> : <p>Nenhuma proxy disponível para estes filtros.</p>}
    {list.data && !list.isError && <OperationalPagination label="proxies disponíveis" page={options.page} pageSize={options.pageSize} total={list.data.total} busy={busy || list.isFetching} onPage={page => { setSelected(''); setOptions(current => ({ ...current, page })) }} onSize={pageSize => { setSelected(''); setOptions(current => ({ ...current, page: 1, pageSize })) }} />}
    {error && <p role="alert" className="text-sm text-red-700">{error}</p>}
    <div className="flex justify-end gap-3"><Button variant="outline" disabled={busy} onClick={onCancel}>Cancelar</Button><Button disabled={busy || !selected || list.isFetching || list.isError} onClick={() => void submit()}>{busy ? 'Reservando…' : 'Reservar proxy selecionada'}</Button></div>
  </div>
}
