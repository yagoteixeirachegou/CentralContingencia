import { expect, test } from 'vitest'
import type { AdsPowerGroup, AdsPowerProfile } from './api'
import { ageLabel, filterProfiles, noFilters, sortGroups, sortProfiles } from './filters'
const group = (groupId: string, groupName: string, total: number, oldestCreatedAt: string | null): AdsPowerGroup => ({ groupId, groupName, total, oldestCreatedAt, newestCreatedAt: oldestCreatedAt })
const profile = (profileId: string, name: string, groupId: string, tags: string[], createdAt: string | null, serialNumber: string | null = null): AdsPowerProfile =>
  ({ profileId, name, groupId, groupName: 'G' + groupId, tags, createdAt, serialNumber })
const groups = [group('1', 'FB | FLAG', 15, '2025-01-01T00:00:00Z'), group('2', 'Spark Ads', 9, '2024-06-19T00:00:00Z'), group('3', 'Share-0517', 0, null), group('4', 'IA | VEO3', 15, '2026-05-01T00:00:00Z')]
const names = (list: { groupName: string }[]) => list.map(item => item.groupName)

test.each([
  ['count-desc', ['FB | FLAG', 'IA | VEO3', 'Spark Ads', 'Share-0517']], ['count-asc', ['Share-0517', 'Spark Ads', 'FB | FLAG', 'IA | VEO3']],
  ['name-asc', ['FB | FLAG', 'IA | VEO3', 'Share-0517', 'Spark Ads']], ['name-desc', ['Spark Ads', 'Share-0517', 'IA | VEO3', 'FB | FLAG']],
  ['oldest', ['Spark Ads', 'FB | FLAG', 'IA | VEO3', 'Share-0517']], ['newest', ['IA | VEO3', 'FB | FLAG', 'Spark Ads', 'Share-0517']],
] as const)('ordena grupos por %s (empates por nome; grupos sem data sempre no fim)', (sort, expected) => {
  expect(names(sortGroups(groups, sort))).toEqual(expected)
})
test('ordenar não altera a lista original', () => { const copy = [...groups]; sortGroups(groups, 'name-asc'); expect(groups).toEqual(copy) })

const profiles = [profile('a', 'Perfil 10', '1', ['BACKUP', 'ESTEIRA DE PERFIL'], '2025-03-01T00:00:00Z', '10'), profile('b', 'Perfil 2', '1', ['ESTEIRA DE PERFIL'], '2024-06-19T00:00:00Z', '2'),
  profile('c', 'Édson Ads', '2', [], null, '3'), profile('d', 'perfil 1', '3', ['backup'], '2026-01-01T00:00:00Z', '1')]
const ids = (list: AdsPowerProfile[]) => list.map(item => item.profileId)
test('busca pelo nome sem diferenciar maiúsculas nem acentos', () => {
  expect(ids(filterProfiles(profiles, { ...noFilters, search: 'EDSON' }))).toEqual(['c'])
  expect(ids(filterProfiles(profiles, { ...noFilters, search: ' perfil ' }))).toEqual(['a', 'b', 'd'])
})
test('grupos valem como "qualquer um" e tags como "todas"', () => {
  expect(ids(filterProfiles(profiles, { ...noFilters, groupIds: ['2', '3'] }))).toEqual(['c', 'd'])
  expect(ids(filterProfiles(profiles, { ...noFilters, tags: ['ESTEIRA DE PERFIL'] }))).toEqual(['a', 'b'])
  expect(ids(filterProfiles(profiles, { ...noFilters, tags: ['ESTEIRA DE PERFIL', 'BACKUP'] }))).toEqual(['a'])
  expect(ids(filterProfiles(profiles, { ...noFilters, tags: ['Backup'] }))).toEqual(['a', 'd'])
})
test('filtros combinados se restringem; sem filtros devolve tudo', () => {
  expect(ids(filterProfiles(profiles, { search: 'perfil', groupIds: ['1'], tags: ['BACKUP'] }))).toEqual(['a'])
  expect(filterProfiles(profiles, noFilters)).toHaveLength(4)
})
test('ordena perfis em ordem natural por nome, nos dois sentidos', () => {
  expect(ids(sortProfiles(profiles, 'name', 'asc'))).toEqual(['c', 'd', 'b', 'a'])
  expect(ids(sortProfiles(profiles, 'name', 'desc'))).toEqual(['a', 'b', 'd', 'c'])
})
test('ordena por criação com perfis sem data sempre no fim, e por número', () => {
  expect(ids(sortProfiles(profiles, 'created', 'asc'))).toEqual(['b', 'a', 'd', 'c'])
  expect(ids(sortProfiles(profiles, 'created', 'desc'))).toEqual(['d', 'a', 'b', 'c'])
  expect(ids(sortProfiles(profiles, 'serial', 'asc'))).toEqual(['d', 'b', 'c', 'a'])
})
test('ageLabel descreve a idade em dias, meses e anos', () => {
  const now = new Date('2026-10-12T12:00:00Z')
  expect(ageLabel('2026-10-12T01:00:00Z', now)).toBe('hoje'); expect(ageLabel('2026-10-11T00:00:00Z', now)).toBe('há 1 dia'); expect(ageLabel('2026-10-02T00:00:00Z', now)).toBe('há 10 dias')
  expect(ageLabel('2026-08-01T00:00:00Z', now)).toBe('há 2 meses'); expect(ageLabel('2025-10-01T00:00:00Z', now)).toBe('há 1 ano'); expect(ageLabel('2024-06-19T00:00:00Z', now)).toBe('há 2 anos e 3 meses')
  expect(ageLabel('2027-01-01T00:00:00Z', now)).toBe('agora há pouco'); expect(ageLabel('inválida', now)).toBe('agora há pouco')
})
