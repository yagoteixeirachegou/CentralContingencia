import { useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { confirmAllocation, createProfile, listProfiles, operationalError, refreshOperations, reserveProxy } from '../../profiles/api'
import { OperationalPagination } from '../../profiles/OperationalPagination'
import { ApiError } from '../../../lib/api/client'
import { Input } from '../../../components/ui/input'
import { Select } from '../../../components/ui/select'
import { Button } from '../../../components/ui/button'
import { ListSkeleton } from '../../../components/ui/skeleton'
import { assignments, type GridRow } from './contracts'
type Step = 'check' | 'create' | 'reserve' | 'confirm'
// Composes existing endpoints: create profile (optional) -> reserve -> confirm.
// Progress is kept so a retry resumes at the failed step instead of repeating writes.
export function LinkProfileForm({ row, onBusy, onLinked, onCancel }: { row: GridRow; onBusy(value: boolean): void; onLinked(message: string): void; onCancel(): void }) {
  const client = useQueryClient(); const guard = useRef(false); const key = useRef(crypto.randomUUID())
  const progress = useRef<{ profileId?: string; profileName?: string; created?: boolean; allocationId?: string }>({})
  const [mode, setMode] = useState<'new' | 'existing'>('new')
  const [name, setName] = useState(''); const [platform1, setPlatform1] = useState(''); const [platform2, setPlatform2] = useState('')
  const [search, setSearch] = useState(''); const [filter, setFilter] = useState(''); const [page, setPage] = useState(1); const [pageSize, setSize] = useState(20)
  const [selected, setSelected] = useState<{ id: string; name: string } | null>(null)
  const [busy, setBusy] = useState(false); const [error, setError] = useState(''); const [uncertain, setUncertain] = useState(false); const [started, setStarted] = useState(false)
  const list = useQuery({ queryKey: ['profiles', 'link-to-proxy', filter, page, pageSize], enabled: mode === 'existing',
    queryFn: ({ signal }) => listProfiles({ page, pageSize, name: filter || undefined, status: 'Active', sort: 'name', direction: 'asc' }, signal) })
  const trimmed = name.trim()
  const ready = progress.current.profileId ? true : mode === 'new' ? trimmed.length > 0 && trimmed.length <= 200 : Boolean(selected)
  async function link() {
    if (guard.current || uncertain || !ready) return
    guard.current = true; setBusy(true); onBusy(true); setError('')
    let step: Step = 'check'
    try {
      if (!progress.current.profileId) {
        if (mode === 'existing' && selected) progress.current = { profileId: selected.id, profileName: selected.name }
        else {
          const existing = await listProfiles({ page: 1, pageSize: 1, name: trimmed, status: 'Active', sort: 'name', direction: 'asc' })
          if (existing.total > 0) { setError('Já existe um perfil ativo com esse nome. Use a aba "Perfil existente" para vinculá-lo.'); return }
          step = 'create'; setStarted(true)
          const profile = await createProfile({ name: trimmed, ...(platform1 ? { platform1 } : {}), ...(platform2 ? { platform2 } : {}) })
          progress.current = { profileId: profile.id, profileName: profile.name, created: true }
        }
        setStarted(true)
      }
      if (!progress.current.allocationId) { step = 'reserve'; progress.current.allocationId = (await reserveProxy(progress.current.profileId!, row.proxyId, key.current)).id }
      step = 'confirm'; await confirmAllocation(progress.current.allocationId)
      await refreshOperations(client); onLinked(`${progress.current.profileName} vinculado a ${row.host}:${row.port}.`)
    } catch (reason) {
      const detail = operationalError(reason)
      if (step === 'check' || step === 'create') { setError(detail); setUncertain(step === 'create' && (!(reason instanceof ApiError) || reason.status >= 500)) }
      else if (step === 'reserve') setError(`${progress.current.created ? 'Perfil criado' : 'Perfil selecionado'}, mas a proxy não foi vinculada. ${detail}`)
      else setError(`Reserva criada, mas não confirmada. Tente novamente antes do prazo da reserva ou use "Confirmar" na linha do grid. ${detail}`)
      await refreshOperations(client)
    } finally { guard.current = false; setBusy(false); onBusy(false) }
  }
  const locked = busy || Boolean(progress.current.profileId)
  return <div className="mt-4 space-y-4">
    <p className="text-sm text-slate-600">Proxy <span className="font-medium text-slate-900">{row.host}:{row.port}</span> ({row.region}). A API revalida disponibilidade e exclusividade.</p>
    <div role="tablist" aria-label="Origem do perfil" className="flex gap-2">{(['new', 'existing'] as const).map(value => <Button key={value} type="button" role="tab" aria-selected={mode === value} variant={mode === value ? 'default' : 'outline'} disabled={locked} onClick={() => { setMode(value); setError('') }}>{value === 'new' ? 'Novo perfil' : 'Perfil existente'}</Button>)}</div>
    {mode === 'new' ? <fieldset disabled={locked} className="space-y-3">
      <div><label htmlFor="link-profile-name" className="mb-1 block text-sm font-medium">Nome do perfil</label><Input id="link-profile-name" maxLength={200} value={name} onChange={event => setName(event.target.value)} placeholder="Perfil 1234" /></div>
      <div className="grid gap-3 sm:grid-cols-2">{([['link-platform1', 'Plataforma 1', platform1, setPlatform1], ['link-platform2', 'Plataforma 2', platform2, setPlatform2]] as const).map(([id, label, value, set]) => <div key={id}><label htmlFor={id} className="mb-1 block text-sm">{label}</label><Select id={id} value={value} onChange={event => set(event.target.value)}><option value="">Sem atribuição</option>{assignments.map(item => <option key={item} value={item}>{item}</option>)}</Select></div>)}</div>
    </fieldset> : <div className="space-y-3">
      <form className="flex items-end gap-2" onSubmit={event => { event.preventDefault(); setFilter(search.trim()); setPage(1); setSelected(null) }}><div className="min-w-0 flex-1"><label htmlFor="link-profile-search" className="text-sm">Nome exato do perfil</label><Input id="link-profile-search" maxLength={200} value={search} onChange={event => setSearch(event.target.value)} disabled={locked} /></div><Button variant="outline" disabled={locked}>Pesquisar</Button></form>
      {list.isPending ? <ListSkeleton label="Carregando perfis…" rows={3} /> : list.isError ? <p role="alert" className="text-red-700">{operationalError(list.error)}</p> : !list.data?.items.length ? <p className="text-sm">Nenhum perfil ativo encontrado.</p> : <fieldset disabled={locked || list.isFetching} className="space-y-2"><legend className="mb-2 text-sm font-medium">Perfil ativo</legend>{list.data.items.map(profile => <label key={profile.id} className="flex gap-2 text-sm"><input type="radio" name="link-profile" value={profile.id} checked={selected?.id === profile.id} onChange={() => setSelected({ id: profile.id, name: profile.name })} />{profile.name}</label>)}</fieldset>}
      {list.data && <OperationalPagination label="perfis" page={page} pageSize={pageSize} total={list.data.total} busy={locked || list.isFetching} onPage={value => { setPage(value); setSelected(null) }} onSize={value => { setSize(value); setPage(1); setSelected(null) }} />}
    </div>}
    {error && <p role="alert" className="text-sm text-red-700">{error}</p>}
    {uncertain && <p className="text-sm text-slate-600">Resultado incerto. Feche e confira o grid antes de tentar novamente.</p>}
    <div className="flex justify-end gap-2"><Button type="button" variant="outline" disabled={busy} onClick={onCancel}>{started ? 'Fechar' : 'Cancelar'}</Button><Button type="button" disabled={busy || uncertain || !ready} onClick={() => void link()}>{busy ? 'Vinculando…' : started ? 'Tentar novamente' : 'Vincular'}</Button></div>
  </div>
}
