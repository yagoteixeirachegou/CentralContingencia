import { ArrowDown, ArrowUp } from 'lucide-react'
import { ageLabel, type Direction, type ProfileSortKey } from './filters'
import type { AdsPowerProfile } from './api'
const date = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short' })
const columns: { key: ProfileSortKey; label: string }[] = [{ key: 'serial', label: 'Nº' }, { key: 'name', label: 'Perfil' }, { key: 'group', label: 'Grupo' }]
export function ProfileTable({ profiles, sort, direction, now, onSort, onTag }: { profiles: AdsPowerProfile[]; sort: ProfileSortKey; direction: Direction; now: Date; onSort(key: ProfileSortKey): void; onTag(tag: string): void }) {
  const header = (key: ProfileSortKey, label: string) => <th scope="col" aria-sort={sort === key ? direction === 'asc' ? 'ascending' : 'descending' : undefined} className="px-3 py-2.5 font-medium">
    <button type="button" onClick={() => onSort(key)} className="inline-flex items-center gap-1 rounded text-left hover:text-slate-950 focus-visible:outline-2 focus-visible:outline-sky-700">{label}{sort === key && (direction === 'asc' ? <ArrowUp size={13} aria-hidden="true" /> : <ArrowDown size={13} aria-hidden="true" />)}</button></th>
  return <div className="overflow-x-auto"><table className="w-full min-w-[720px] text-left text-sm">
    <thead className="border-y border-slate-200 bg-slate-50 text-xs text-slate-600"><tr>{columns.map(column => header(column.key, column.label))}<th scope="col" className="px-3 py-2.5 font-medium">Tags</th>{header('created', 'Criado em')}</tr></thead>
    <tbody className="divide-y divide-slate-100">{profiles.map(profile => <tr key={profile.profileId} className="hover:bg-slate-50">
      <td className="px-3 py-2 tabular-nums text-slate-500">{profile.serialNumber ?? '—'}</td>
      <td className="px-3 py-2 font-medium text-slate-900">{profile.name}</td>
      <td className="px-3 py-2 text-slate-700">{profile.groupName}</td>
      <td className="px-3 py-2"><div className="flex flex-wrap gap-1">{profile.tags.length === 0 ? <span className="text-slate-400">—</span> : profile.tags.map(tag =>
        <button key={tag} type="button" onClick={() => onTag(tag)} title={`Filtrar pela tag ${tag}`} className="rounded-md bg-slate-100 px-1.5 py-0.5 text-xs text-slate-700 hover:bg-slate-200 focus-visible:outline-2 focus-visible:outline-sky-700">{tag}</button>)}</div></td>
      <td className="whitespace-nowrap px-3 py-2 text-slate-700">{profile.createdAt ? <><time dateTime={profile.createdAt}>{date.format(new Date(profile.createdAt))}</time><span className="block text-xs text-slate-500">{ageLabel(profile.createdAt, now)}</span></> : '—'}</td>
    </tr>)}</tbody></table></div>
}
