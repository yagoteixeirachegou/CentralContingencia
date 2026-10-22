import { ageLabel } from './filters'
import type { AdsPowerGroup } from './api'
const number = new Intl.NumberFormat('pt-BR')
const percent = new Intl.NumberFormat('pt-BR', { style: 'percent', maximumFractionDigits: 0 })
const date = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short' })
// Blocos compactos: clicar seleciona o grupo como filtro da lista de perfis (vários grupos valem como "qualquer um").
export function GroupTiles({ groups, total, selected, now, onToggle }: { groups: AdsPowerGroup[]; total: number; selected: string[]; now: Date; onToggle(groupId: string): void }) {
  const biggest = Math.max(1, ...groups.map(group => group.total))
  return <ul className="grid grid-cols-2 gap-2 md:grid-cols-3 xl:grid-cols-4">{groups.map(group => {
    const on = selected.includes(group.groupId)
    return <li key={group.groupId}><button type="button" aria-pressed={on} aria-label={`${group.groupName}: ${group.total} ${group.total === 1 ? 'perfil' : 'perfis'}`}
      title={group.oldestCreatedAt ? `Perfil mais antigo do grupo: ${date.format(new Date(group.oldestCreatedAt))}` : 'Sem datas de criação'}
      onClick={() => onToggle(group.groupId)}
      className={`flex h-full w-full flex-col gap-1.5 rounded-lg border px-3 py-2 text-left transition-colors focus-visible:outline-2 focus-visible:outline-sky-700 ${on ? 'border-sky-800 bg-sky-50' : 'border-slate-200 bg-surface hover:bg-slate-50'} ${group.total === 0 ? 'opacity-60' : ''}`}>
      <span className="line-clamp-2 break-words text-sm font-medium leading-snug text-slate-900" aria-hidden="true">{group.groupName}</span>
      <span className="flex items-baseline justify-between gap-2 tabular-nums" aria-hidden="true"><span className="text-lg font-semibold leading-none text-slate-950">{number.format(group.total)}</span><span className="text-xs text-slate-500">{percent.format(total > 0 ? group.total / total : 0)}</span></span>
      <span className="h-1 overflow-hidden rounded-full bg-slate-100" aria-hidden="true"><span className="block h-full rounded-full bg-sky-800" style={{ width: `${(group.total / biggest) * 100}%` }} /></span>
      <span className="truncate text-xs text-slate-500" aria-hidden="true">{group.oldestCreatedAt ? `mais antigo ${ageLabel(group.oldestCreatedAt, now)}` : 'sem datas'}</span>
    </button></li>
  })}</ul>
}
