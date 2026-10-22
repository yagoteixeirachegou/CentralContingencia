import { useEffect, useRef, useState } from 'react'
import { AccessDenied } from '../../../components/layout/StatusPages'
import { Link, useLocation } from 'react-router-dom'
import { gridReturn } from '../grid/navigation'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useAuth } from '../../auth/auth-context'
import { ApiError } from '../../../lib/api/client'
import { Button } from '../../../components/ui/button'
import { Input } from '../../../components/ui/input'
import { Dialog } from '../../../components/ui/dialog'
import { commitCsv, failureReport, importError, previewCsv } from './api'
import { fileError, importFormSchema, issueText, pageSize, type CsvPreview, type CsvReport, type ImportForm } from './contracts'
import { ImportPreviewTable, ImportReport } from './ImportPreviewTable'

export function ProxyImportPage() {
  const returnTo = gridReturn(useLocation().state)
  const { user } = useAuth()
  const client = useQueryClient()
  const form = useForm<ImportForm>({ resolver: zodResolver(importFormSchema), defaultValues: { defaultRegion: '', skipRowsWithoutConnection: false, referenceYear: '' } })
  const [file, setFile] = useState<File>()
  const [preview, setPreview] = useState<CsvPreview>()
  const [report, setReport] = useState<CsvReport>()
  const [error, setError] = useState('')
  const [confirming, setConfirming] = useState(false)
  const [completed, setCompleted] = useState(false)
  const [now, setNow] = useState(Date.now())
  const [reviewed, setReviewed] = useState<Record<number, boolean>>({})
  const key = useRef<string | undefined>(undefined)
  const lock = useRef(false)
  const controller = useRef<AbortController | undefined>(undefined)
  const selectedRegion = form.watch('defaultRegion')
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 1000); return () => { clearInterval(timer); controller.current?.abort() } }, [])
  const expiresAt = Date.parse(preview?.confirmationExpiresAt ?? '')
  const expired = !Number.isFinite(expiresAt) || expiresAt <= now
  const pages = Math.max(1, Math.ceil((preview?.summary.total ?? 0) / pageSize))
  const blocking = preview?.headerIssues.some(issue => ['MissingHeader', 'DuplicateHeader'].includes(issue.code))
  // Summary counts include duplicates/empty as invalid. When ambiguous, inspect every page.
  const ignorableOnly = preview && preview.summary.invalid === preview.summary.duplicates + preview.summary.empty
    && Object.keys(reviewed).length === pages && Object.values(reviewed).every(Boolean)
  const eligible = Boolean(preview?.confirmationToken && preview.summary.total > 0 && !blocking && (preview.summary.invalid === 0 || ignorableOnly))
  const invalidatePreview = (newOperation = true) => {
    setPreview(undefined); setReport(undefined); setError(''); setReviewed({}); setConfirming(false); setCompleted(false)
    if (newOperation) key.current = undefined
  }
  const analyze = useMutation({ gcTime: 0, retry: false, mutationFn: async (page: number) => {
    if (!file) return
    setError(''); setConfirming(false)
    controller.current = new AbortController()
    try {
      const result = await previewCsv(file, form.getValues('defaultRegion'), page, controller.current.signal, form.getValues('skipRowsWithoutConnection'), form.getValues('referenceYear'))
      setPreview(result); setNow(Date.now()); setReport(undefined)
      setReviewed(previous => ({ ...previous, [page]: result.items.every(row => row.errors.every(issue => ['EmptyRow', 'DuplicateRecord'].includes(issue.code))) }))
    } catch (failure) { setError(importError(failure)); setPreview(undefined); setReviewed({}) }
    finally { lock.current = false }
  } })
  const commit = useMutation({ gcTime: 0, retry: false, mutationFn: async () => {
    if (!file || !preview?.confirmationToken) return
    setError('')
    key.current ??= crypto.randomUUID()
    controller.current = new AbortController()
    try {
      const result = await commitCsv(file, form.getValues('defaultRegion'), preview.confirmationToken, key.current, controller.current.signal, form.getValues('skipRowsWithoutConnection'), form.getValues('referenceYear'))
      setReport(result); setCompleted(true); setFile(undefined); setPreview(undefined)
      void client.invalidateQueries({ queryKey: ['proxies'] })
    } catch (failure) {
      setError(importError(failure)); setReport(failureReport(failure))
      if (failure instanceof ApiError && [409, 422].includes(failure.status)) { setPreview(undefined); setReviewed({}) }
    } finally { lock.current = false; setConfirming(false) }
  } })
  const busy = analyze.isPending || commit.isPending
  const startPreview = (page: number) => {
    if (!file || lock.current) return
    lock.current = true; analyze.mutate(page)
  }
  const startCommit = () => {
    if (!file || !preview?.confirmationToken || !eligible || Date.now() >= expiresAt || expired || lock.current || completed) return
    lock.current = true; commit.mutate()
  }
  if (!user?.roles.includes('Admin')) return <AccessDenied reason="Somente administradores podem importar CSV." />
  return <section className="space-y-6">
    <header><Link to={returnTo} className="text-sm text-link">Voltar para proxies</Link><h1 className="mt-3 text-3xl font-semibold text-slate-950">Importar CSV</h1><p className="mt-2 text-sm text-slate-600">Selecione, revise a prévia e confirme explicitamente. Proxies existentes não serão sobrescritos.</p></header>
    <form className="space-y-4 rounded-2xl border border-slate-200 bg-surface p-5" onSubmit={form.handleSubmit(() => {
      const validation = fileError(file)
      if (validation) { setError(validation); return }
      if (!lock.current) { setReviewed({}); startPreview(1) }
    })}>
      <div><label htmlFor="csv-file" className="mb-2 block text-sm font-medium">Arquivo CSV</label>
        <Input id="csv-file" type="file" accept=".csv,text/csv" disabled={busy} onChange={event => {
          invalidatePreview(); const chosen = event.target.files?.[0]; const validation = fileError(chosen)
          setFile(validation ? undefined : chosen); if (validation) setError(validation)
        }} /></div>
      <p className="text-xs text-slate-600">UTF-8, vírgula ou ponto e vírgula. Limite inicial: 2 MiB; a API aplica seus próprios limites. Não há envio automático.</p>
      <p className="text-sm text-slate-600">Datas dd/MM exigem ano de referência explícito. Datas completas são preservadas. PERFIL, PLATAFORMA e ADsPower Lote não serão importados.</p>
      <div><label htmlFor="csv-year" className="mb-2 block text-sm font-medium">Ano de referência (opcional)</label>
        <Input id="csv-year" type="text" inputMode="numeric" disabled={busy} aria-describedby="csv-year-help" aria-invalid={Boolean(form.formState.errors.referenceYear)} {...form.register('referenceYear', { onChange: () => invalidatePreview() })} />
        <p id="csv-year-help" className="mt-2 text-xs text-slate-600">Informe um ano inteiro entre 1 e 9999, como 2026, somente para datas dd/MM sem ano. Nenhum ano é selecionado automaticamente; o backend valida calendário e vencimento futuro.</p>
        {form.formState.errors.referenceYear && <p role="alert" className="mt-2 text-sm text-red-700">{form.formState.errors.referenceYear.message}</p>}</div>
      <div><label htmlFor="csv-region" className="mb-2 block text-sm font-medium">Região complementar (opcional)</label>
        <Input id="csv-region" disabled={busy} {...form.register('defaultRegion', { onChange: () => invalidatePreview() })} />
        <p className="mt-2 text-xs text-slate-600">Informe explicitamente quando REGIÃO estiver ausente ou vazia. Valores já presentes no CSV serão preservados; a API valida a região.</p></div>
      <Button type="submit" disabled={!file || busy}>{analyze.isPending ? 'Analisando CSV…' : 'Gerar prévia'}</Button>
      <label className="flex items-start gap-2 text-sm"><input type="checkbox" disabled={busy} {...form.register('skipRowsWithoutConnection', { onChange: () => invalidatePreview() })} />Ignorar explicitamente linhas com host, porta, usuário e senha todos vazios</label>
      <p className="text-xs text-slate-600">Linhas parcialmente preenchidas continuam bloqueando a importação. As linhas ignoradas serão avisadas e registradas no relatório.</p>
    </form>
    {busy && <p role="status" aria-live="polite">{commit.isPending ? 'Processando importação. Aguarde a resposta da API…' : 'Enviando e validando o arquivo…'}</p>}
    {error && <p role="alert" className="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-800">{error}</p>}
    {preview && <>
      <div className="rounded-2xl border border-slate-200 bg-surface p-5"><h2 className="text-xl font-semibold">Resumo do arquivo completo</h2>
        <p className="mt-3 text-sm">Total: {preview.summary.total} · Válidos: {preview.summary.valid} · Inválidos: {preview.summary.invalid} · Duplicados: {preview.summary.duplicates} · Conflitantes: {preview.summary.conflicts} · Vazios/sem conexão ignoráveis: {preview.summary.empty}</p>
        <p className="mt-2 text-xs text-slate-600">Duplicatas e vazios integram inválidos na prévia, mas podem ser ignorados na confirmação. Conflitos são ignorados, nunca sobrescritos.</p>
        {preview.headerIssues.map((issue, index) => <p key={index} className="mt-2 text-sm">{issueText(issue, preview.columns)}</p>)}
      </div>
      <ImportPreviewTable preview={preview} region={selectedRegion} busy={busy} onPage={startPreview} />
      {expired && <p role="alert">A prova expirou. Gere uma nova prévia antes de confirmar.</p>}
      {!eligible && <p className="text-sm text-slate-600">Corrija os erros ou revise todas as páginas para verificar se há apenas duplicatas/linhas vazias ignoráveis. A API revalidará todo o arquivo.</p>}
      <Button disabled={busy || expired || !eligible || completed} onClick={() => setConfirming(true)}>Revisar confirmação</Button>
    </>}
    {report && <ImportReport key={report.importId ?? 'invalid'} report={report} />}
    {completed && <p className="text-sm text-emerald-800">Importação concluída. A listagem de proxies foi atualizada. Selecione outro arquivo para uma nova operação.</p>}
    <Dialog open={confirming} busy={commit.isPending} onOpenChange={setConfirming} title="Confirmar importação CSV" description="Esta ação grava os registros válidos após nova validação. Registros existentes não serão alterados.">
      {expired && <p role="alert" className="mt-4 text-sm text-red-700">A prova expirou. Cancele e gere uma nova prévia.</p>}
      <p className="mt-4 text-sm">Deseja importar este arquivo com as opções revisadas? Erros de validação impedem a gravação de todo o arquivo; vazios, duplicatas válidas e conflitos serão ignorados.</p>
      {form.getValues('skipRowsWithoutConnection') && <p className="mt-3 text-sm">Você autorizou ignorar linhas sem qualquer dado de conexão; elas serão registradas como ignoradas.</p>}
      {form.getValues('referenceYear') && <p className="mt-3 text-sm">Ano de referência: {form.getValues('referenceYear')} — aplicado somente às datas dd/MM sem ano.</p>}
      <div className="mt-5 flex gap-3"><Button variant="outline" disabled={commit.isPending} onClick={() => setConfirming(false)}>Cancelar</Button>
        <Button disabled={busy || expired || !eligible || completed} onClick={startCommit}>{commit.isPending ? 'Importando…' : 'Confirmar importação'}</Button></div>
    </Dialog>
  </section>
}
