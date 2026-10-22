import { useEffect, useState } from 'react'
import { Input } from '../../../components/ui/input'
import { Select } from '../../../components/ui/select'
import { Button } from '../../../components/ui/button'
import { assignments, availabilityLabels, type GridOptions } from './contracts'
function Search({ name, label, value, maxLength, onValue }: { name: string; label: string; value: string; maxLength: number; onValue(value: string): void }) {
  const [draft, setDraft] = useState(value)
  useEffect(() => setDraft(value), [value])
  useEffect(() => {
    if (draft === value) return
    const timer = setTimeout(() => { setDraft(draft.trim()); onValue(draft.trim()) }, 350)
    return () => clearTimeout(timer)
  }, [draft, value, onValue])
  return <div><label htmlFor={`grid-${name}`} className="mb-1 block text-xs font-medium text-slate-600">{label}</label><Input id={`grid-${name}`} value={draft} maxLength={maxLength} onChange={event => setDraft(event.target.value)} className="text-sm" /></div>
}
export function GridFilters({ options, onProfile, onRegion, onOption, onClear }: {
  options: GridOptions; onProfile(value: string): void; onRegion(value: string): void
  onOption(name: string, value: string): void; onClear(): void
}) {
  return <div className="grid items-end gap-3 p-4 sm:grid-cols-2 xl:grid-cols-[1fr_1fr_1fr_1fr_1fr_auto]">
    <Search name="profile" label="Perfil" value={options.profile ?? ''} maxLength={200} onValue={onProfile} />
    <Search name="region" label="Região" value={options.region ?? ''} maxLength={100} onValue={onRegion} />
    <div><label htmlFor="grid-platform" className="mb-1 block text-xs font-medium text-slate-600">Plataforma</label><Select id="grid-platform" className="text-sm" value={options.platform ?? ''} onChange={event => onOption('platform', event.target.value)}><option value="">Todas</option>{assignments.map(value => <option key={value} value={value}>{value}</option>)}</Select></div>
    <div><label htmlFor="grid-availability" className="mb-1 block text-xs font-medium text-slate-600">Disponibilidade</label><Select id="grid-availability" className="text-sm" value={options.availability ?? ''} onChange={event => onOption('availability', event.target.value)}><option value="">Todas</option>{Object.entries(availabilityLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</Select></div>
    <div><label htmlFor="grid-direction" className="mb-1 block text-xs font-medium text-slate-600">Vencimento</label><Select id="grid-direction" className="text-sm" value={options.direction} onChange={event => onOption('direction', event.target.value)}><option value="asc">Crescente</option><option value="desc">Decrescente</option></Select></div>
    <Button variant="outline" className="text-sm" onClick={onClear}>Limpar</Button>
  </div>
}
