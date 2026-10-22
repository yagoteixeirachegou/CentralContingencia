import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, ClipboardCopy, KeyRound, ListChecks, Plus, RefreshCw, ShieldOff } from 'lucide-react'
import { AccessDenied } from '../../components/layout/StatusPages'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { Dialog } from '../../components/ui/dialog'
import { Input } from '../../components/ui/input'
import { TableSkeleton } from '../../components/ui/skeleton'
import { useAuth } from '../auth/auth-context'
import { profileRead } from '../profiles/contracts'
import { connectionLabel, listExecutors, provisionExecutor, revokeExecutor, rotateExecutor, stateLabel, taskError, type Executor, type ProvisionedExecutor } from './api'

const tone: Record<Executor['connection'], string> = {
  Online: 'border-emerald-300 bg-emerald-50 text-emerald-800', Offline: 'border-slate-300 bg-slate-100 text-slate-600', Revoked: 'border-red-300 bg-red-50 text-red-800',
}
const format = (value: string | null) => value ? new Date(value).toLocaleString('pt-BR') : 'nunca'
type Action = { kind: 'rotate' | 'revoke'; executor: Executor }

export function ExecutorsPage() {
  const { user } = useAuth()
  const queryClient = useQueryClient()
  const allowed = profileRead(user?.roles)
  const isAdmin = user?.roles.includes('Admin') ?? false
  const [creating, setCreating] = useState(false)
  const [name, setName] = useState('')
  const [action, setAction] = useState<Action | null>(null)
  // A credencial só existe aqui, na memória desta tela, até o diálogo ser fechado.
  const [issued, setIssued] = useState<ProvisionedExecutor | null>(null)
  const [copied, setCopied] = useState<'ok' | 'error' | ''>('')
  const [message, setMessage] = useState('')

  const query = useQuery({ queryKey: ['executors'], queryFn: ({ signal }) => listExecutors(signal), enabled: allowed, refetchInterval: 5000, retry: false })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['executors'] })
  const provision = useMutation({
    mutationFn: () => provisionExecutor(name.trim()),
    onSuccess: async result => { setCreating(false); setName(''); setIssued(result); setCopied(''); await refresh() },
    onError: reason => setMessage(taskError(reason, 'Não foi possível criar o executor. O nome pode já estar em uso.')),
  })
  const change = useMutation({
    mutationFn: async (current: Action) => current.kind === 'rotate' ? await rotateExecutor(current.executor.id) : (await revokeExecutor(current.executor.id), null),
    onSuccess: async result => {
      setAction(null); if (result) { setIssued(result); setCopied('') } else setMessage('Executor revogado: ele não consegue mais reservar nem alterar tarefas.')
      await refresh()
    },
    onError: reason => { setMessage(taskError(reason)); setAction(null) },
  })
  if (!allowed) return <AccessDenied reason="Seu papel não permite consultar os executores." />

  const copy = async () => {
    try { await navigator.clipboard.writeText(issued!.credential); setCopied('ok') } catch { setCopied('error') }
  }
  const submit = (event: FormEvent) => { event.preventDefault(); setMessage(''); if (name.trim().length >= 3) provision.mutate() }

  return <section className="space-y-5">
    <header className="flex flex-wrap items-end justify-between gap-3">
      <div><h1 className="text-3xl font-semibold text-slate-950">Executores</h1>
        <p className="mt-1 text-sm text-slate-600">Workers autorizados a executar tarefas. Online = comunicou recentemente. A lista atualiza a cada 5 segundos.</p></div>
      <div className="flex flex-wrap items-center gap-2">
        <Link to="/tasks" className="inline-flex items-center gap-2 rounded-md border border-slate-300 bg-surface px-4 py-2 text-sm font-medium text-slate-800 hover:bg-slate-100"><ListChecks size={16} aria-hidden="true" />Tarefas</Link>
        <Button variant="outline" className="gap-2 text-sm" onClick={() => void query.refetch()}><RefreshCw size={16} aria-hidden="true" />Atualizar</Button>
        {isAdmin && <Button className="gap-2 text-sm" onClick={() => { setMessage(''); setCreating(true) }}><Plus size={16} aria-hidden="true" />Novo executor</Button>}
      </div>
    </header>
    {message && <p role="status" className="rounded-lg border border-slate-200 bg-surface px-4 py-3 text-sm text-slate-700">{message}</p>}
    <div className="overflow-x-auto rounded-2xl border border-slate-200 bg-surface">
      {query.isPending ? <TableSkeleton label="Carregando executores…" columns={6} rows={4} />
        : query.isError ? <div className="p-6"><p role="alert" className="mb-3 text-red-700">{taskError(query.error, 'Não foi possível carregar os executores.')}</p><Button variant="outline" onClick={() => void query.refetch()}>Tentar novamente</Button></div>
        : query.data.length === 0 ? <p className="p-6 text-sm text-slate-600">Nenhum executor provisionado.{isAdmin ? ' Use “Novo executor” para criar o primeiro.' : ''}</p>
        : <table className="w-full min-w-[820px] text-left text-sm">
          <caption className="sr-only">Executores</caption>
          <thead className="border-b border-slate-200 text-xs uppercase tracking-wide text-slate-500"><tr>
            <th className="px-4 py-3">Executor</th><th className="px-4 py-3">Conexão</th><th className="px-4 py-3">Estado</th><th className="px-4 py-3">Tarefas em execução</th>
            <th className="px-4 py-3">Última comunicação</th><th className="px-4 py-3">Versão</th>{isAdmin && <th className="px-4 py-3"><span className="sr-only">Ações</span></th>}</tr></thead>
          <tbody>{query.data.map(executor => <tr key={executor.id} className="border-b border-slate-100 last:border-0">
            <td className="px-4 py-3"><p className="font-medium text-slate-900">{executor.name}</p><p className="text-xs text-slate-500">{executor.allowedTypes.join(', ')}</p></td>
            <td className="px-4 py-3"><Badge className={tone[executor.connection]}>{connectionLabel[executor.connection]}</Badge></td>
            <td className="px-4 py-3">{executor.connection === 'Revoked' ? '—' : stateLabel[executor.state]}</td>
            <td className="px-4 py-3">{executor.runningTasks}</td>
            <td className="px-4 py-3">{format(executor.lastSeenAt)}</td>
            <td className="px-4 py-3">{executor.workerVersion ?? '—'}</td>
            {isAdmin && <td className="px-4 py-3 text-right"><span className="inline-flex gap-2">{executor.status === 'Active' && <>
              <Button variant="outline" className="gap-1 px-3 py-1.5 text-xs" aria-label={`Rotacionar credencial de ${executor.name}`} onClick={() => setAction({ kind: 'rotate', executor })}><KeyRound size={14} aria-hidden="true" />Rotacionar</Button>
              <Button variant="outline" className="gap-1 px-3 py-1.5 text-xs" aria-label={`Revogar ${executor.name}`} onClick={() => setAction({ kind: 'revoke', executor })}><ShieldOff size={14} aria-hidden="true" />Revogar</Button></>}</span></td>}
          </tr>)}</tbody></table>}
    </div>

    <Dialog open={creating} onOpenChange={setCreating} busy={provision.isPending} title="Novo executor" description="Cada worker tem identidade e credencial próprias. A credencial será exibida uma única vez.">
      <form onSubmit={submit} className="mt-5 space-y-4">
        <div><label htmlFor="executor-name" className="text-sm font-medium text-slate-700">Nome do executor</label>
          <Input id="executor-name" value={name} maxLength={100} autoComplete="off" placeholder="ex.: win-adspower-01" onChange={event => setName(event.target.value)} />
          <p className="mt-1 text-xs text-slate-500">Letras, números, espaço, ponto, hífen e sublinhado; mínimo de 3 caracteres.</p></div>
        <div className="flex justify-end gap-2"><Button type="button" variant="outline" disabled={provision.isPending} onClick={() => setCreating(false)}>Cancelar</Button>
          <Button type="submit" disabled={provision.isPending || name.trim().length < 3}>{provision.isPending ? 'Criando…' : 'Criar executor'}</Button></div>
      </form>
    </Dialog>

    <Dialog open={action !== null} onOpenChange={open => { if (!open) setAction(null) }} busy={change.isPending}
      title={action?.kind === 'rotate' ? 'Rotacionar credencial?' : 'Revogar executor?'}
      description={action?.kind === 'rotate'
        ? `A credencial atual de “${action.executor.name}” deixa de valer imediatamente; o worker precisa receber a nova.`
        : `“${action?.executor.name}” não poderá mais reservar nem alterar tarefas. A revogação é definitiva.`}>
      <div className="mt-6 flex justify-end gap-2"><Button variant="outline" disabled={change.isPending} onClick={() => setAction(null)}>Voltar</Button>
        <Button disabled={change.isPending} onClick={() => action && change.mutate(action)}>{change.isPending ? 'Aplicando…' : action?.kind === 'rotate' ? 'Rotacionar' : 'Revogar'}</Button></div>
    </Dialog>

    <Dialog open={issued !== null} onOpenChange={open => { if (!open) { setIssued(null); setCopied('') } }} title="Credencial do executor"
      description="Copie agora e guarde no cofre do worker. Ela não será exibida novamente e não pode ser recuperada; se perder, rotacione.">
      <div className="mt-4 space-y-3">
        <p className="text-sm font-medium text-slate-800">{issued?.executor.name}</p>
        <input readOnly aria-label="Credencial" value={issued?.credential ?? ''} onFocus={event => event.currentTarget.select()} className="w-full rounded-md border border-slate-300 bg-slate-50 px-3 py-2 font-mono text-xs" />
        <div className="flex items-center justify-between gap-2">
          <Button variant="outline" className="gap-2 text-sm" onClick={() => void copy()}>{copied === 'ok' ? <Check size={16} aria-hidden="true" /> : <ClipboardCopy size={16} aria-hidden="true" />}Copiar credencial</Button>
          <Button onClick={() => { setIssued(null); setCopied('') }}>Já guardei</Button></div>
        {copied === 'ok' && <p role="status" className="text-sm text-emerald-700">Credencial copiada.</p>}
        {copied === 'error' && <p role="alert" className="text-sm text-red-700">Não foi possível copiar automaticamente. Selecione o texto e copie manualmente.</p>}
      </div>
    </Dialog>
  </section>
}
