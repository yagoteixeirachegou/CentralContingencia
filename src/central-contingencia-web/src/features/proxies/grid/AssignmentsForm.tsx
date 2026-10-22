import { useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { operationalError, refreshOperations, updateProfileAssignments } from '../../profiles/api'
import { Select } from '../../../components/ui/select'
import { Button } from '../../../components/ui/button'
import { assignments, type GridRow } from './contracts'
// Sends only changed slots: omitted slots are preserved, an emptied slot is sent as null.
export function AssignmentsForm({ row, onBusy, onSaved, onCancel }: { row: GridRow; onBusy(value: boolean): void; onSaved(message: string): void; onCancel(): void }) {
  const client = useQueryClient(); const guard = useRef(false)
  const [platform1, setPlatform1] = useState(row.platform1 ?? ''); const [platform2, setPlatform2] = useState(row.platform2 ?? '')
  const [busy, setBusy] = useState(false); const [error, setError] = useState('')
  const changes = { ...(platform1 !== (row.platform1 ?? '') ? { platform1: platform1 || null } : {}), ...(platform2 !== (row.platform2 ?? '') ? { platform2: platform2 || null } : {}) }
  const changed = Object.keys(changes).length > 0
  async function save() {
    if (guard.current || !changed || !row.profileId) return
    guard.current = true; setBusy(true); onBusy(true); setError('')
    try { await updateProfileAssignments(row.profileId, changes); await refreshOperations(client); onSaved(`Plataformas de ${row.profileName} atualizadas.`) }
    catch (reason) { setError(operationalError(reason)); await refreshOperations(client) }
    finally { guard.current = false; setBusy(false); onBusy(false) }
  }
  return <div className="mt-4 space-y-4">
    <fieldset disabled={busy} className="grid gap-3 sm:grid-cols-2">{([['assign-platform1', 'Plataforma 1', platform1, setPlatform1], ['assign-platform2', 'Plataforma 2', platform2, setPlatform2]] as const).map(([id, label, value, set]) => <div key={id}><label htmlFor={id} className="mb-1 block text-sm">{label}</label><Select id={id} value={value} onChange={event => set(event.target.value)}><option value="">Sem atribuição</option>{assignments.map(item => <option key={item} value={item}>{item}</option>)}</Select></div>)}</fieldset>
    {error && <p role="alert" className="text-sm text-red-700">{error}</p>}
    <div className="flex justify-end gap-2"><Button type="button" variant="outline" disabled={busy} onClick={onCancel}>Cancelar</Button><Button type="button" disabled={busy || !changed} onClick={() => void save()}>{busy ? 'Salvando…' : 'Salvar plataformas'}</Button></div>
  </div>
}
