import { Button } from '../../components/ui/button'
import { Select } from '../../components/ui/select'
import { useId } from 'react'
export function OperationalPagination({ page, pageSize, total, busy, onPage, onSize, label }: {
  page: number; pageSize: number; total: number; busy: boolean; label: string
  onPage(page: number): void; onSize(size: number): void
}) {
  const pages = Math.min(10000, Math.max(1, Math.ceil(total / pageSize)))
  const sizeId = useId()
  return <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 p-4">
    <div className="flex items-center gap-2 text-sm"><label htmlFor={sizeId}>Por página</label><Select id={sizeId} className="w-auto text-sm" value={pageSize} disabled={busy} onChange={event => onSize(Number(event.target.value))}>{[20, 50, 100].map(size => <option key={size} value={size}>{size}</option>)}</Select></div>
    <div className="flex items-center gap-3 text-sm"><Button variant="outline" disabled={busy || page <= 1} aria-label={`Página anterior de ${label}`} onClick={() => onPage(page - 1)}>Anterior</Button><span>Página {page} de {pages} · {total} resultado(s)</span><Button variant="outline" disabled={busy || page >= pages} aria-label={`Próxima página de ${label}`} onClick={() => onPage(page + 1)}>Próxima</Button></div>
  </div>
}
