import { useEffect, useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Check, ClipboardCopy, Search } from 'lucide-react'
import { AccessDenied } from '../../components/layout/StatusPages'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { TableSkeleton } from '../../components/ui/skeleton'
import { useAuth } from '../auth/auth-context'
import { dashboardError, getAdsPowerDashboard } from '../dashboard/api'
import { profileRead } from '../profiles/contracts'
import {
  buildWarmupPrompt,
  findDefaultGroup,
  toWarmupInput,
  type WarmupMode,
} from './verificationPrompt'

const maxSelection = 20

export function WarmupPage() {
  const { user } = useAuth()
  const allowed = profileRead(user?.roles)

  const query = useQuery({
    queryKey: ['adspower', 'dashboard'],
    queryFn: ({ signal }) => getAdsPowerDashboard(signal),
    enabled: allowed,
    staleTime: 60_000,
    retry: false,
    refetchOnWindowFocus: false,
  })

  const [groupId, setGroupId] = useState('')
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<string[]>([])
  const [copied, setCopied] = useState<'ok' | 'error' | ''>('')
  const [mode, setMode] = useState<WarmupMode>('verification')
  const [duration, setDuration] = useState('5m30s')
  const [likes, setLikes] = useState(5)

  const data = query.data

  useEffect(() => {
    if (data && !groupId) setGroupId(findDefaultGroup(data.groups)?.groupId ?? '')
  }, [data, groupId])

  const profiles = useMemo(() => {
    if (!data || !groupId) return []
    const term = search.trim().toLowerCase()
    return data.profiles.filter(
      profile =>
        profile.groupId === groupId &&
        (!term || profile.name.toLowerCase().includes(term)),
    )
  }, [data, groupId, search])

  const chosenGroup = useMemo(
    () => data?.groups.find(group => group.groupId === groupId),
    [data, groupId],
  )

  const chosen = useMemo(() => {
    if (!data || !chosenGroup) return []
    return data.profiles
      .filter(profile => selected.includes(profile.profileId))
      .map(profile => toWarmupInput(profile, chosenGroup.groupName))
  }, [data, chosenGroup, selected])

  if (!allowed) {
    return <AccessDenied reason="Seu papel não permite consultar os perfis do AdsPower." />
  }

  const allVisible =
    profiles.length > 0 &&
    profiles.every(profile => selected.includes(profile.profileId))

  const toggle = (id: string) => {
    setCopied('')
    setSelected(current =>
      current.includes(id)
        ? current.filter(item => item !== id)
        : current.length >= maxSelection
          ? current
          : [...current, id],
    )
  }

  const toggleAll = () => {
    setCopied('')
    setSelected(current =>
      allVisible
        ? current.filter(id => !profiles.some(profile => profile.profileId === id))
        : [...new Set([...current, ...profiles.map(profile => profile.profileId)])].slice(0, maxSelection),
    )
  }

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(
        buildWarmupPrompt(chosen, { mode, duration, likes }),
      )
      setCopied('ok')
    } catch {
      setCopied('error')
    }
  }

  const defaultGroup = data ? findDefaultGroup(data.groups) : undefined

  return (
    <section className="space-y-5">
      <header>
        <h1 className="text-3xl font-semibold text-slate-950">Aquecimento</h1>
        <p className="mt-1 text-sm text-slate-600">
          Escolha os perfis do grupo e o modo. A verificação é somente leitura: abre uma página,
          observa e fecha o perfil. A sessão completa abre o Feed, assiste vídeos e curte alguns
          conteúdos.
        </p>
      </header>

      {query.isPending ? (
        <div className="rounded-2xl border border-slate-200 bg-surface">
          <TableSkeleton label="Lendo os perfis do AdsPower…" columns={4} rows={8} />
        </div>
      ) : query.isError ? (
        <div className="rounded-2xl border border-slate-200 bg-surface p-6">
          <p role="alert" className="mb-3 text-red-700">{dashboardError(query.error)}</p>
          <Button variant="outline" onClick={() => void query.refetch()}>
            Tentar novamente
          </Button>
        </div>
      ) : data ? (
        <>
          <div className="flex flex-wrap items-end gap-3 rounded-2xl border border-slate-200 bg-surface p-4">
            <div className="min-w-[240px]">
              <label htmlFor="warmup-group" className="mb-1 block text-sm font-medium text-slate-700">
                Grupo do AdsPower
              </label>
              <Select
                id="warmup-group"
                value={groupId}
                onChange={event => {
                  setGroupId(event.target.value)
                  setSelected([])
                  setCopied('')
                }}
              >
                <option value="">Selecione um grupo</option>
                {data.groups.map(group => (
                  <option key={group.groupId} value={group.groupId}>
                    {group.groupName} ({group.total})
                  </option>
                ))}
              </Select>
            </div>

            <div className="min-w-[220px] flex-1">
              <label htmlFor="warmup-search" className="mb-1 block text-sm font-medium text-slate-700">
                Buscar perfil pelo nome
              </label>
              <div className="relative">
                <Search
                  size={16}
                  className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-slate-400"
                  aria-hidden="true"
                />
                <Input
                  id="warmup-search"
                  className="pl-9"
                  value={search}
                  maxLength={200}
                  onChange={event => setSearch(event.target.value)}
                />
              </div>
            </div>

            <div className="min-w-[160px]">
              <label htmlFor="warmup-mode" className="mb-1 block text-sm font-medium text-slate-700">
                Modo
              </label>
              <Select
                id="warmup-mode"
                value={mode}
                onChange={event => {
                  setMode(event.target.value as WarmupMode)
                  setCopied('')
                }}
              >
                <option value="verification">Verificação</option>
                <option value="session">Sessão completa</option>
              </Select>
            </div>

            {mode === 'session' && (
              <>
                <div className="min-w-[140px]">
                  <label htmlFor="warmup-duration" className="mb-1 block text-sm font-medium text-slate-700">
                    Duração
                  </label>
                  <Input
                    id="warmup-duration"
                    value={duration}
                    maxLength={16}
                    onChange={event => {
                      setDuration(event.target.value)
                      setCopied('')
                    }}
                  />
                </div>

                <div className="min-w-[120px]">
                  <label htmlFor="warmup-likes" className="mb-1 block text-sm font-medium text-slate-700">
                    Curtidas
                  </label>
                  <Input
                    id="warmup-likes"
                    type="number"
                    min={0}
                    max={20}
                    value={likes}
                    onChange={event => {
                      const next = Number(event.target.value) || 0
                      setLikes(Math.max(0, Math.min(20, next)))
                      setCopied('')
                    }}
                  />
                </div>
              </>
            )}
          </div>

          {!defaultGroup && (
            <p role="status" className="rounded-lg bg-amber-50 p-3 text-sm text-amber-900">
              O grupo “FB | ESTEIRA DE PERFIS” não foi encontrado no AdsPower. Escolha o grupo na lista.
            </p>
          )}

          {!groupId ? (
            <div className="rounded-2xl border border-slate-200 bg-surface p-8 text-center">
              <h2 className="font-semibold">Selecione um grupo</h2>
              <p className="mt-1 text-sm text-slate-500">
                Os perfis do grupo aparecem aqui para você marcar quais serão verificados.
              </p>
            </div>
          ) : profiles.length === 0 ? (
            <div className="rounded-2xl border border-slate-200 bg-surface p-8 text-center">
              <h2 className="font-semibold">Nenhum perfil encontrado</h2>
              <p className="mt-1 text-sm text-slate-500">
                Este grupo não tem perfis ou a busca não encontrou nenhum.
              </p>
            </div>
          ) : (
            <div className="overflow-hidden rounded-2xl border border-slate-200 bg-surface">
              <div className="flex flex-wrap items-center justify-between gap-3 p-4">
                <p role="status" className="text-sm text-slate-600">
                  {selected.length} de {profiles.length} selecionados
                  {selected.length >= maxSelection ? ` (limite de ${maxSelection} por vez)` : ''}
                </p>
                <Button className="gap-2" disabled={chosen.length === 0} onClick={() => void copy()}>
                  {copied === 'ok' ? (
                    <Check size={16} aria-hidden="true" />
                  ) : (
                    <ClipboardCopy size={16} aria-hidden="true" />
                  )}
                  {copied === 'ok'
                    ? 'Prompt copiado'
                    : mode === 'verification'
                      ? 'Copiar prompt de verificação'
                      : 'Copiar prompt de sessão'}
                </Button>
              </div>

              {copied === 'error' && (
                <p role="alert" className="mx-4 mb-3 rounded-lg bg-red-50 p-3 text-sm text-red-800">
                  Não foi possível copiar. Permita o acesso à área de transferência e tente de novo.
                </p>
              )}

              <div className="overflow-x-auto">
                <table className="w-full min-w-[560px] text-left text-sm">
                  <thead className="border-y border-slate-200 bg-slate-50 text-xs text-slate-600">
                    <tr>
                      <th scope="col" className="w-10 px-3 py-2.5">
                        <input
                          type="checkbox"
                          aria-label="Selecionar todos os perfis listados"
                          checked={allVisible}
                          onChange={toggleAll}
                          className="size-4 accent-sky-800"
                        />
                      </th>
                      <th scope="col" className="px-3 py-2.5 font-medium">Nº</th>
                      <th scope="col" className="px-3 py-2.5 font-medium">Perfil</th>
                      <th scope="col" className="px-3 py-2.5 font-medium">Tags</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {profiles.map(profile => {
                      const on = selected.includes(profile.profileId)
                      return (
                        <tr key={profile.profileId} className={on ? 'bg-sky-50' : 'hover:bg-slate-50'}>
                          <td className="px-3 py-2">
                            <input
                              type="checkbox"
                              aria-label={`Selecionar ${profile.name}`}
                              checked={on}
                              onChange={() => toggle(profile.profileId)}
                              className="size-4 accent-sky-800"
                            />
                          </td>
                          <td className="px-3 py-2 tabular-nums text-slate-500">
                            {profile.serialNumber ?? '—'}
                          </td>
                          <td className="px-3 py-2 font-medium text-slate-900">{profile.name}</td>
                          <td className="px-3 py-2 text-slate-600">
                            {profile.tags.length === 0 ? '—' : profile.tags.join(', ')}
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          <p className="text-xs text-slate-500">
            Os perfis vêm da leitura do AdsPower (somente leitura). Nada é executado por esta tela:
            o prompt copiado é colado no AI Agent.
          </p>
        </>
      ) : null}
    </section>
  )
}
