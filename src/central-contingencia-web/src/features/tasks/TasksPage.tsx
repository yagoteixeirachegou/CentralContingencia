import { useState } from 'react'
import { Link } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Ban, RefreshCw, Server } from 'lucide-react'
import { AccessDenied } from '../../components/layout/StatusPages'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { Dialog } from '../../components/ui/dialog'
import { Select } from '../../components/ui/select'
import { TableSkeleton } from '../../components/ui/skeleton'
import { useAuth } from '../auth/auth-context'
import { profileRead, profileWrite } from '../profiles/contracts'
import { cancelTask, listTasks, statusLabel, taskError, type TaskStatus, type WorkTask } from './api'

const pageSize = 20
const statuses = Object.keys(statusLabel) as TaskStatus[]
const tone: Record<TaskStatus, string> = {
  Pending: 'border-slate-300 bg-slate-50 text-slate-700', Running: 'border-sky-200 bg-sky-50 text-link', Succeeded: 'border-emerald-300 bg-emerald-50 text-emerald-800',
  Failed: 'border-red-300 bg-red-50 text-red-800', Cancelled: 'border-slate-300 bg-slate-100 text-slate-600', DeadLetter: 'border-amber-300 bg-amber-50 text-amber-800',
}
const format = (value: string | null) => value ? new Date(value).toLocaleString('pt-BR') : '—'

export function TasksPage() {
  const { user } = useAuth()
  const queryClient = useQueryClient()
  const allowed = profileRead(user?.roles)
  const canCancel = profileWrite(user?.roles)
  const [status, setStatus] = useState<TaskStatus | ''>('')
  const [page, setPage] = useState(1)
  const [target, setTarget] = useState<WorkTask | null>(null)
  const [message, setMessage] = useState('')

  // Atualiza sozinho: o estado das tarefas muda sem ação do operador.
  const query = useQuery({
    queryKey: ['tasks', status, page], queryFn: ({ signal }) => listTasks({ page, pageSize, status: status || undefined }, signal),
    enabled: allowed, placeholderData: keepPreviousData, refetchInterval: 5000, retry: false,
  })
  const cancel = useMutation({
    mutationFn: (task: WorkTask) => cancelTask(task.id),
    onSuccess: async (task) => {
      setMessage(task.status === 'Cancelled' ? 'Tarefa cancelada.' : 'Cancelamento solicitado: o executor interrompe a tarefa no próximo heartbeat.')
      setTarget(null); await queryClient.invalidateQueries({ queryKey: ['tasks'] })
    },
    onError: reason => { setMessage(taskError(reason)); setTarget(null) },
  })
  if (!allowed) return <AccessDenied reason="Seu papel não permite consultar as tarefas." />

  const data = query.data
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1
  return <section className="space-y-5">
    <header className="flex flex-wrap items-end justify-between gap-3">
      <div><h1 className="text-3xl font-semibold text-slate-950">Tarefas</h1>
        <p className="mt-1 text-sm text-slate-600">Fila de tarefas executadas pelos workers. A lista atualiza a cada 5 segundos.</p></div>
      <div className="flex flex-wrap items-center gap-2">
        <Link to="/tasks/executors" className="inline-flex items-center gap-2 rounded-md border border-slate-300 bg-surface px-4 py-2 text-sm font-medium text-slate-800 hover:bg-slate-100"><Server size={16} aria-hidden="true" />Executores</Link>
        <Button variant="outline" className="gap-2 text-sm" onClick={() => void query.refetch()}><RefreshCw size={16} aria-hidden="true" />Atualizar</Button>
      </div>
    </header>
    <div className="max-w-xs"><label className="text-sm font-medium text-slate-700" htmlFor="task-status">Estado</label>
      <Select id="task-status" value={status} onChange={event => { setStatus(event.target.value as TaskStatus | ''); setPage(1) }}>
        <option value="">Todos</option>{statuses.map(item => <option key={item} value={item}>{statusLabel[item]}</option>)}</Select></div>
    {message && <p role="status" className="rounded-lg border border-slate-200 bg-surface px-4 py-3 text-sm text-slate-700">{message}</p>}
    <div className="overflow-x-auto rounded-2xl border border-slate-200 bg-surface">
      {query.isPending ? <TableSkeleton label="Carregando tarefas…" columns={7} rows={8} />
        : query.isError ? <div className="p-6"><p role="alert" className="mb-3 text-red-700">{taskError(query.error, 'Não foi possível carregar as tarefas.')}</p><Button variant="outline" onClick={() => void query.refetch()}>Tentar novamente</Button></div>
        : data && data.items.length === 0 ? <p className="p-6 text-sm text-slate-600">Nenhuma tarefa {status ? `no estado “${statusLabel[status]}”` : 'registrada'}.</p>
        : <table className="w-full min-w-[860px] text-left text-sm">
          <caption className="sr-only">Tarefas da fila</caption>
          <thead className="border-b border-slate-200 text-xs uppercase tracking-wide text-slate-500"><tr>
            <th className="px-4 py-3">Tarefa</th><th className="px-4 py-3">Estado</th><th className="px-4 py-3">Executor</th><th className="px-4 py-3">Tentativas</th>
            <th className="px-4 py-3">Criada</th><th className="px-4 py-3">Última atividade</th><th className="px-4 py-3">Erro</th><th className="px-4 py-3"><span className="sr-only">Ações</span></th></tr></thead>
          <tbody>{data?.items.map(task => <tr key={task.id} className="border-b border-slate-100 align-top last:border-0">
            <td className="px-4 py-3"><p className="font-medium text-slate-900">{task.type}</p><p className="font-mono text-xs text-slate-500" title={task.id}>{task.id.slice(0, 8)}</p></td>
            <td className="px-4 py-3"><Badge className={tone[task.status]}>{statusLabel[task.status]}</Badge>
              {task.cancelRequested && task.status === 'Running' && <p className="mt-1 text-xs text-amber-700">cancelamento solicitado</p>}
              {task.nextAttemptAt && task.attemptCount > 0 && <p className="mt-1 text-xs text-slate-500">nova tentativa {format(task.nextAttemptAt)}</p>}</td>
            <td className="px-4 py-3">{task.executorName ?? (task.executorId ? 'Executor removido' : '—')}</td>
            <td className="px-4 py-3">{task.attemptCount}/{task.maxAttempts}</td>
            <td className="px-4 py-3">{format(task.createdAt)}</td>
            <td className="px-4 py-3">{format(task.lastActivityAt)}</td>
            <td className="px-4 py-3 font-mono text-xs">{task.lastErrorCode ?? '—'}</td>
            <td className="px-4 py-3 text-right">{canCancel && (task.status === 'Pending' || (task.status === 'Running' && !task.cancelRequested)) &&
              <Button variant="outline" className="gap-1 px-3 py-1.5 text-xs" aria-label={`Cancelar tarefa ${task.id.slice(0, 8)}`} onClick={() => setTarget(task)}><Ban size={14} aria-hidden="true" />Cancelar</Button>}</td>
          </tr>)}</tbody></table>}
    </div>
    {data && data.total > 0 && <nav aria-label="Paginação" className="flex items-center justify-between text-sm text-slate-600">
      <span>{data.total} tarefa(s) · página {data.page} de {pages}</span>
      <span className="flex gap-2"><Button variant="outline" disabled={page <= 1} onClick={() => setPage(page - 1)}>Anterior</Button>
        <Button variant="outline" disabled={page >= pages} onClick={() => setPage(page + 1)}>Próxima</Button></span></nav>}
    <Dialog open={target !== null} onOpenChange={open => { if (!open) setTarget(null) }} busy={cancel.isPending} title="Cancelar tarefa?"
      description={target?.status === 'Running' ? 'A tarefa está em execução: o executor será avisado e interrompe no próximo heartbeat.' : 'A tarefa sairá da fila e não será executada.'}>
      <div className="mt-6 flex justify-end gap-2"><Button variant="outline" disabled={cancel.isPending} onClick={() => setTarget(null)}>Voltar</Button>
        <Button disabled={cancel.isPending} onClick={() => target && cancel.mutate(target)}>{cancel.isPending ? 'Cancelando…' : 'Cancelar tarefa'}</Button></div>
    </Dialog>
  </section>
}
