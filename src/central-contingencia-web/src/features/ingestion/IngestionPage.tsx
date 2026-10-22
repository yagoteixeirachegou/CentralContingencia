import { useEffect, useRef, useState, type DragEvent } from 'react'
import { AccessDenied } from '../../components/layout/StatusPages'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { FileText, UploadCloud, X } from 'lucide-react'
import { useAuth } from '../auth/auth-context'
import { Button } from '../../components/ui/button'
import { Dialog } from '../../components/ui/dialog'
import { CardsSkeleton, Skeleton } from '../../components/ui/skeleton'
import { getStatus, submitFiles, uploadError, validateFiles, type UploadReport } from './api'

const badge = 'inline-flex rounded-full border px-3 py-1 text-xs font-bold tracking-wide'

export function IngestionPage() {
  const { user } = useAuth()
  const client = useQueryClient()
  const allowed = Boolean(user?.roles.some(role => role === 'Admin' || role === 'Manager'))
  const input = useRef<HTMLInputElement>(null)
  const lock = useRef(false)
  const [files, setFiles] = useState<File[]>([])
  const [problem, setProblem] = useState('')
  const [report, setReport] = useState<UploadReport>()
  const [confirming, setConfirming] = useState(false)
  const [dragging, setDragging] = useState(false)
  const [elapsed, setElapsed] = useState(0)
  const status = useQuery({ queryKey: ['ingestion', 'status'], queryFn: ({ signal }) => getStatus(signal), enabled: allowed, retry: false, refetchOnWindowFocus: false })
  const limits = status.data ?? { maxFiles: 20, maxFileBytes: 512 * 1024 }

  const send = useMutation({ gcTime: 0, retry: false, mutationFn: async () => {
    setProblem(''); setReport(undefined)
    try {
      const result = await submitFiles(files)
      setReport(result); setFiles([])
      void client.invalidateQueries({ queryKey: ['proxies'] }); void client.invalidateQueries({ queryKey: ['dashboard'] })
    } catch (failure) { setProblem(uploadError(failure)) }
    finally { lock.current = false; setConfirming(false); void client.invalidateQueries({ queryKey: ['ingestion', 'status'] }) }
  } })
  // Fechar ou recarregar a aba no meio do envio perderia o acompanhamento do resultado.
  useEffect(() => {
    if (!send.isPending) return
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = '' }
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [send.isPending])
  useEffect(() => {
    if (!send.isPending) { setElapsed(0); return }
    const startedAt = Date.now(); const timer = window.setInterval(() => setElapsed(Math.floor((Date.now() - startedAt) / 1000)), 1000)
    return () => window.clearInterval(timer)
  }, [send.isPending])

  const add =(incoming: FileList | File[]) => {
    const merged = [...files]
    for (const file of Array.from(incoming)) if (!merged.some(existing => existing.name.toLowerCase() === file.name.toLowerCase())) merged.push(file)
    const invalid = validateFiles(merged, limits)
    setReport(undefined)
    if (invalid) { setProblem(invalid); return }
    setProblem(''); setFiles(merged)
  }
  const onDrop = (event: DragEvent) => { event.preventDefault(); setDragging(false); if (!send.isPending) add(event.dataTransfer.files) }
  const start = () => { if (lock.current || !files.length) return; lock.current = true; setConfirming(false); send.mutate() }

  if (!allowed) return <AccessDenied reason="Somente Admin e Manager podem enviar perfis." />
  const data = status.data
  const production = data?.group.mode === 'Production'
  const blocked = !data || !data.configured || data.inProgress
  const beyondProxies = data ? Math.max(0, files.length - data.availableProxies) : 0

  return <section className="space-y-6">
    <header><Link to="/profiles" className="text-sm text-link">Voltar para perfis</Link>
      <h1 className="mt-3 text-3xl font-semibold text-slate-950">Novos perfis — envio de TXT</h1>
      <p className="mt-2 text-sm text-slate-600">Os arquivos passam só pela memória da Central até o n8n: nada é gravado em disco, banco ou log. Cada TXT vira um perfil no AdsPower, com uma proxy livre.</p></header>

    {status.isPending && <CardsSkeleton label="Consultando proxies livres e a numeração no AdsPower…" />}
    {status.isError && <p role="alert" className="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-800">{uploadError(status.error)} Sem essa consulta o envio fica bloqueado.
      <Button variant="outline" className="ml-3" onClick={() => void status.refetch()}>Tentar de novo</Button></p>}
    {data && <div className="grid gap-4 rounded-2xl border border-slate-200 bg-surface p-5 sm:grid-cols-4" aria-label="Situação atual">
      <div><p className="text-xs text-slate-600">Ambiente</p>
        <span className={`${badge} ${production ? 'border-red-300 bg-red-50 text-red-800' : 'border-amber-300 bg-amber-50 text-amber-900'}`}>{production ? 'PRODUÇÃO' : 'TESTE'}</span></div>
      <div><p className="text-xs text-slate-600">Grupo no AdsPower</p><p className="font-medium">{data.group.name}</p></div>
      <div><p className="text-xs text-slate-600">Proxies livres</p><p className="text-2xl font-semibold">{data.availableProxies}</p></div>
      <div><p className="text-xs text-slate-600">Próximo perfil</p><p className="font-medium">{data.nextProfileName}</p></div>
    </div>}
    {data && !data.configured && <p role="alert" className="rounded-xl border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900">O envio ao n8n ainda não foi configurado neste computador (webhook e chave). Veja docs/n8n/README.md.</p>}
    {data?.inProgress && !send.isPending && <p role="status" className="rounded-xl border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900">Há um envio em andamento. Aguarde terminar para enviar outro.
      <Button variant="outline" className="ml-3" onClick={() => void status.refetch()}>Atualizar</Button></p>}

    <div className="space-y-4 rounded-2xl border border-slate-200 bg-surface p-5">
      <div onDragOver={event => { event.preventDefault(); setDragging(true) }} onDragLeave={() => setDragging(false)} onDrop={onDrop}
        className={`flex flex-col items-center gap-3 rounded-xl border-2 border-dashed p-8 text-center ${dragging ? 'border-sky-600 bg-sky-50' : 'border-slate-300'}`}>
        <UploadCloud size={32} aria-hidden className="text-slate-500" />
        <p className="text-sm">Arraste os arquivos .txt aqui ou</p>
        <input ref={input} id="txt-files" type="file" multiple accept=".txt,text/plain" className="sr-only" aria-label="Arquivos TXT" disabled={send.isPending}
          onChange={event => { if (event.target.files) add(event.target.files); event.target.value = '' }} />
        <Button variant="outline" disabled={send.isPending} onClick={() => input.current?.click()}>Escolher arquivos</Button>
        <p className="text-xs text-slate-600">Até {limits.maxFiles} arquivos de {Math.round(limits.maxFileBytes / 1024)} KB. Cada TXT cria um perfil.</p>
      </div>
      {problem && <p role="alert" className="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-800">{problem}</p>}
      {files.length > 0 && <>
        <ul className="divide-y divide-slate-200 rounded-xl border border-slate-200" aria-label="Arquivos selecionados">
          {files.map(file => <li key={file.name} className="flex items-center justify-between gap-3 px-4 py-2 text-sm">
            <span className="flex items-center gap-2"><FileText size={16} aria-hidden />{file.name}<span className="text-xs text-slate-500">{Math.max(1, Math.round(file.size / 1024))} KB</span></span>
            <button type="button" aria-label={`Remover ${file.name}`} disabled={send.isPending} className="rounded p-1 hover:bg-slate-100 disabled:opacity-50"
              onClick={() => { setFiles(files.filter(item => item !== file)); setProblem('') }}><X size={16} /></button></li>)}
        </ul>
        {beyondProxies > 0 && <p className="text-sm text-amber-900">Há {data?.availableProxies} proxies livres para {files.length} arquivos: {beyondProxies} ficarão sem perfil (nenhum é perdido, apenas não preenchido).</p>}
        <div className="flex flex-wrap gap-3">
          <Button disabled={blocked || send.isPending} onClick={() => setConfirming(true)}>Revisar e enviar</Button>
          <Button variant="outline" disabled={send.isPending} onClick={() => { setFiles([]); setProblem('') }}>Limpar seleção</Button>
        </div>
      </>}
    </div>

    {send.isPending && <Sending files={files} elapsed={elapsed} />}
    {report && <Result report={report} />}

    <Dialog open={confirming} busy={send.isPending} onOpenChange={setConfirming} title="Confirmar envio" description="Esta ação cria perfis de verdade no AdsPower e reserva proxies.">
      <p className="mt-4 text-sm">Ambiente: <strong>{production ? 'PRODUÇÃO' : 'TESTE'}</strong> — grupo <strong>{data?.group.name}</strong>.</p>
      <p className="mt-2 text-sm">{files.length} arquivo(s). O primeiro perfil será <strong>{data?.nextProfileName}</strong>{beyondProxies > 0 ? `; ${beyondProxies} ficarão sem proxy.` : '.'}</p>
      {production && <p role="alert" className="mt-3 rounded-lg bg-red-50 p-3 text-sm text-red-800">Atenção: este é o ambiente de produção.</p>}
      <p className="mt-3 text-xs text-slate-600">Não há reenvio automático. Se algo falhar antes da criação, as proxies são liberadas sozinhas.</p>
      <div className="mt-5 flex gap-3"><Button variant="outline" disabled={send.isPending} onClick={() => setConfirming(false)}>Cancelar</Button>
        <Button disabled={send.isPending || blocked} onClick={start}>{send.isPending ? 'Enviando…' : 'Enviar agora'}</Button></div>
    </Dialog>
  </section>
}

function Result({ report }: { report: UploadReport }) {
  const tone = report.failed ? 'border-red-200 bg-red-50 text-red-900' : report.summary && (report.summary.adsPowerFailures || report.summary.unfilledForLackOfProxy || report.summary.filesForReview) ? 'border-amber-300 bg-amber-50 text-amber-900' : 'border-emerald-200 bg-emerald-50 text-emerald-900'
  const cell = 'px-3 py-2 text-left text-sm'
  return <div className="space-y-4" aria-label="Resultado do envio">
    <div role="status" className={`rounded-2xl border p-5 ${tone}`}>
      <h2 className="text-xl font-semibold">{report.title}</h2>
      <p className="mt-2 text-sm">{report.message}</p>
      {report.summary && <Goal created={report.summary.created} requested={report.summary.requested} />}
      {report.summary && <p className="mt-3 text-sm">Pedidos: {report.summary.requested} · Reservados: {report.summary.reserved} · Criados: {report.summary.created} · Falhas no AdsPower: {report.summary.adsPowerFailures} · Sem proxy: {report.summary.unfilledForLackOfProxy} · Para revisão: {report.summary.filesForReview}</p>}
    </div>
    {report.created.length > 0 && <div className="overflow-x-auto rounded-2xl border border-slate-200 bg-surface"><table className="w-full"><caption className="p-3 text-left font-semibold">Perfis criados</caption>
      <thead><tr><th className={cell}>Perfil</th><th className={cell}>ID no AdsPower</th><th className={cell}>Série</th><th className={cell}>Confirmado na Central</th></tr></thead>
      <tbody>{report.created.map(item => <tr key={item.profile} className="border-t border-slate-200"><td className={cell}>{item.profile}</td><td className={cell}>{item.adsPowerId ?? '—'}</td><td className={cell}>{item.serial ?? '—'}</td>
        <td className={cell}>{item.confirmedInCentral ? 'Sim' : <span className="text-red-800">Não — {item.warning ?? 'confira a reserva'}</span>}</td></tr>)}</tbody></table></div>}
    {report.failures.length > 0 && <div className="rounded-2xl border border-slate-200 bg-surface p-4"><h3 className="font-semibold">Falharam ao criar no AdsPower</h3>
      <ul className="mt-2 list-disc pl-5 text-sm">{report.failures.map(item => <li key={item.profile}>{item.profile}: {item.adsPowerMessage ?? item.reason ?? 'recusado'} — {item.proxyReleased ? 'proxy liberada' : 'proxy será liberada em até 15 minutos'}</li>)}</ul></div>}
    {report.unfilled.length > 0 && <div className="rounded-2xl border border-slate-200 bg-surface p-4"><h3 className="font-semibold">Não preenchidos por falta de proxy</h3>
      <ul className="mt-2 list-disc pl-5 text-sm">{report.unfilled.map((item, index) => <li key={index}>{item.file ?? 'arquivo'} — {item.message ?? item.reason}</li>)}</ul>
      <p className="mt-2 text-xs text-slate-600">Reenvie estes arquivos quando houver proxies livres.</p></div>}
    {report.review.length > 0 && <div className="rounded-2xl border border-slate-200 bg-surface p-4"><h3 className="font-semibold">Precisam de revisão manual</h3>
      <ul className="mt-2 list-disc pl-5 text-sm">{report.review.map(item => <li key={item.file}>{item.file}: {item.reasons.join(' ') || 'sem detalhe'}</li>)}</ul></div>}
  </div>
}


const clock = (seconds: number) => String(Math.floor(seconds / 60)).padStart(2, '0') + ':' + String(seconds % 60).padStart(2, '0')

// O n8n devolve o resultado só no fim, então não há contagem real por perfil durante o envio: mostramos o tempo decorrido e uma estimativa
// (cada perfil espera o limite de chamadas do AdsPower) e deixamos claro que é estimativa.
function Sending({ files, elapsed }: { files: File[]; elapsed: number }) {
  const estimate = 10 + files.length * 6
  return <div role="status" aria-live="polite" className="space-y-3 rounded-2xl border border-sky-200 bg-sky-50 p-5">
    <p className="text-sm font-medium">Processando {files.length} arquivo(s) no n8n e no AdsPower. Não feche esta página nem reenvie.</p>
    <p className="text-sm">Tempo decorrido {clock(elapsed)} · estimativa de cerca de {clock(estimate)} (pode variar)</p>
    <ul className="space-y-2" aria-label="Arquivos em processamento">{files.map(file => <li key={file.name} className="flex items-center gap-3 text-sm"><Skeleton className="h-4 w-4 shrink-0 rounded-full" /><span className="truncate">{file.name}</span><Skeleton className="ml-auto h-4 w-24" /></li>)}</ul>
  </div>
}

// Meta visível: quanto do pedido já foi concluído e quanto falta.
function Goal({ created, requested }: { created: number; requested: number }) {
  const percent = requested > 0 ? Math.round((created / requested) * 100) : 0
  return <div className="mt-3">
    <div role="progressbar" aria-label="Perfis criados" aria-valuemin={0} aria-valuemax={requested} aria-valuenow={created} className="h-2 w-full overflow-hidden rounded-full bg-white/60"><div className="h-full rounded-full bg-current" style={{ width: percent + '%' }} /></div>
    <p className="mt-1 text-sm font-medium">{created} de {requested} perfis criados{requested - created > 0 ? ' — faltam ' + (requested - created) : ''}</p>
  </div>
}
