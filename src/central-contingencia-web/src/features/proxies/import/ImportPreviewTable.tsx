import { useState } from 'react'
import { Button } from '../../../components/ui/button'
import { statusLabels } from '../contracts'
import { issueText, outcomeLabels, type CsvPreview, type CsvReport } from './contracts'
export function ImportPreviewTable({ preview, region, busy, onPage }: { preview: CsvPreview; region: string; busy: boolean; onPage(page: number): void }) {
  const pages = Math.max(1, Math.ceil(preview.summary.total / preview.pageSize))
  return <div className="overflow-hidden rounded-2xl border border-slate-200 bg-surface">
    <div className="overflow-x-auto"><table className="w-full text-left text-sm"><caption className="p-4 text-left font-medium">Prévia do CSV — dados mascarados</caption>
      <thead className="border-y border-slate-200 bg-slate-50"><tr>{['Linha original', 'Proxy', 'Porta', 'Região', 'Status', 'Vencimento interpretado (UTC)', 'Validação'].map(label => <th className="p-3" key={label}>{label}</th>)}</tr></thead>
      <tbody>{preview.items.map(row => <tr key={row.line} className="border-b border-slate-100">
        <td className="p-3">{row.line}{row.endLine !== row.line && `–${row.endLine}`}</td><td className="p-3">{row.hostMasked ?? '—'}</td><td className="p-3">{row.port ?? '—'}</td>
        <td className="p-3">{row.regionPresent ? 'Informada no CSV' : region ? `Complementar: ${region}` : 'Ausente'}</td>
        <td className="p-3">{statusLabels[row.status as keyof typeof statusLabels] ?? '—'}</td>
        <td className="p-3">{row.expiresAt && Number.isFinite(Date.parse(row.expiresAt)) ? new Intl.DateTimeFormat('pt-BR', { timeZone: 'UTC', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' }).format(new Date(row.expiresAt)) + ' UTC' : '—'}</td>
        <td className="p-3">{row.warnings.some(issue => issue.code === 'NoConnectionRowSkipped') ? 'Será ignorada: sem conexão' : row.existingConflict ? 'Conflito existente' : row.duplicate ? 'Duplicata' : row.valid ? 'Válido' : 'Inválido'}
          {[...row.errors, ...row.warnings].map((issue, index) => <p className="mt-1 text-xs text-slate-600" key={index}>{issueText(issue, preview.columns)}</p>)}</td>
      </tr>)}</tbody>
    </table></div>
    {!preview.items.length && <p className="p-4">Nenhuma linha para visualizar.</p>}
    <div className="flex flex-wrap items-center justify-between gap-3 p-4 text-sm"><span>Página {preview.page} de {pages}</span><div className="flex gap-2">
      <Button variant="outline" disabled={busy || preview.page <= 1} onClick={() => onPage(preview.page - 1)}>Página anterior</Button>
      <Button variant="outline" disabled={busy || preview.page >= pages} onClick={() => onPage(preview.page + 1)}>Próxima página</Button>
    </div></div>
  </div>
}
export function ImportReport({ report }: { report: CsvReport }) {
  const [visible, setVisible] = useState(100)
  return <section className="space-y-3 rounded-2xl border border-slate-200 bg-surface p-5" aria-label="Relatório da importação">
    <h2 className="text-xl font-semibold">Relatório da importação</h2>
    <p role="status">Importados: {report.imported} · Ignorados: {report.ignored} · Inválidos: {report.invalid} · Conflitantes: {report.conflicting}</p>
    {report.replayed && <p>Operação já concluída. O resultado original foi recuperado sem nova gravação.</p>}
    <ul className="max-h-80 overflow-y-auto text-sm">{report.items.slice(0, visible).map(item => <li className="py-1" key={item.line}>Linha {item.line}: {outcomeLabels[item.outcome]}</li>)}</ul>
    {visible < report.items.length && <Button variant="outline" onClick={() => setVisible(value => value + 100)}>Mostrar mais resultados</Button>}
  </section>
}
