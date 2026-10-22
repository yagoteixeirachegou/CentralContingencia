import type { AdsPowerGroup, AdsPowerProfile } from './api'
// Ordem natural em português: "Perfil 2" vem antes de "Perfil 10", sem diferenciar maiúsculas ou acentos.
const collator = new Intl.Collator('pt-BR', { numeric: true, sensitivity: 'base' })
export const byName = (a: string, b: string) => collator.compare(a, b)
const normalize = (value: string) => value.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()

export type GroupSort = 'count-desc' | 'count-asc' | 'name-asc' | 'name-desc' | 'oldest' | 'newest'
export const groupSortLabels: Record<GroupSort, string> = {
  'count-desc': 'Mais perfis', 'count-asc': 'Menos perfis', 'name-asc': 'Nome (A–Z)', 'name-desc': 'Nome (Z–A)', oldest: 'Mais antigos', newest: 'Mais recentes',
}
// A API não informa a criação do grupo; a idade do grupo é a do perfil mais antigo dele. Grupos sem datas vão para o fim.
export function sortGroups(groups: AdsPowerGroup[], sort: GroupSort): AdsPowerGroup[] {
  const time = (group: AdsPowerGroup) => group.oldestCreatedAt === null ? null : Date.parse(group.oldestCreatedAt)
  const byTime = (direction: 1 | -1) => (a: AdsPowerGroup, b: AdsPowerGroup) => {
    const x = time(a), y = time(b)
    return x === null && y === null ? byName(a.groupName, b.groupName) : x === null ? 1 : y === null ? -1 : (x - y) * direction || byName(a.groupName, b.groupName)
  }
  const compare: Record<GroupSort, (a: AdsPowerGroup, b: AdsPowerGroup) => number> = {
    'count-desc': (a, b) => b.total - a.total || byName(a.groupName, b.groupName),
    'count-asc': (a, b) => a.total - b.total || byName(a.groupName, b.groupName),
    'name-asc': (a, b) => byName(a.groupName, b.groupName), 'name-desc': (a, b) => byName(b.groupName, a.groupName),
    oldest: byTime(1), newest: byTime(-1),
  }
  return [...groups].sort(compare[sort])
}

export interface ProfileFilters { search: string; groupIds: string[]; tags: string[] }
export const noFilters: ProfileFilters = { search: '', groupIds: [], tags: [] }
export const activeFilters = (filters: ProfileFilters) => (filters.search.trim() ? 1 : 0) + filters.groupIds.length + filters.tags.length
// Busca por nome (parte do texto); grupos selecionados valem como "qualquer um"; tags selecionadas valem como "todas".
export function filterProfiles(profiles: AdsPowerProfile[], filters: ProfileFilters): AdsPowerProfile[] {
  const search = normalize(filters.search.trim()); const tags = filters.tags.map(normalize)
  return profiles.filter(profile => (!search || normalize(profile.name).includes(search)) &&
    (filters.groupIds.length === 0 || filters.groupIds.includes(profile.groupId)) &&
    tags.every(tag => profile.tags.some(own => normalize(own) === tag)))
}

export type ProfileSortKey = 'name' | 'serial' | 'group' | 'created'
export type Direction = 'asc' | 'desc'
export function sortProfiles(profiles: AdsPowerProfile[], key: ProfileSortKey, direction: Direction): AdsPowerProfile[] {
  const sign = direction === 'asc' ? 1 : -1
  const compare = (a: AdsPowerProfile, b: AdsPowerProfile) => {
    if (key === 'name') return byName(a.name, b.name)
    if (key === 'group') return byName(a.groupName, b.groupName) || byName(a.name, b.name)
    if (key === 'serial') return byName(a.serialNumber ?? '', b.serialNumber ?? '')
    const x = a.createdAt === null ? null : Date.parse(a.createdAt), y = b.createdAt === null ? null : Date.parse(b.createdAt)
    return x === null && y === null ? 0 : x === null ? 2 * sign : y === null ? -2 * sign : x - y
  }
  return [...profiles].sort((a, b) => compare(a, b) * sign || byName(a.name, b.name))
}

// "há 3 dias", "há 4 meses", "há 1 ano e 4 meses". Datas futuras ou inválidas viram "agora há pouco".
export function ageLabel(from: string, now: Date): string {
  const days = Math.floor((now.getTime() - Date.parse(from)) / 86_400_000)
  if (!Number.isFinite(days) || days < 0) return 'agora há pouco'
  if (days < 1) return 'hoje'
  if (days < 30) return `há ${days} ${days === 1 ? 'dia' : 'dias'}`
  const months = Math.floor(days / 30.4375)
  if (months < 12) return `há ${months} ${months === 1 ? 'mês' : 'meses'}`
  const years = Math.floor(months / 12), rest = months % 12
  return `há ${years} ${years === 1 ? 'ano' : 'anos'}${rest > 0 ? ` e ${rest} ${rest === 1 ? 'mês' : 'meses'}` : ''}`
}
