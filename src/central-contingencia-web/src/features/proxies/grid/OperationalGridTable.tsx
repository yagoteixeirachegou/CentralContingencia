import { useMemo } from 'react'
import { Link } from 'react-router-dom'
import { flexRender, getCoreRowModel, useReactTable, type ColumnDef } from '@tanstack/react-table'
import { statusLabels } from '../contracts'
import { RowActions } from './RowActions'
import { availabilityLabels, type GridOptions, type GridRow } from './contracts'
const date = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' })
export function OperationalGridTable({ items, options, total, evaluatedAt, busy, canWrite, actionBusy, returnTo, onEdit, onDetails, onLink, onAssignments, onConfirm, onRelease }: {
  items: GridRow[]; options: GridOptions; total: number; evaluatedAt: string; busy: boolean; canWrite: boolean; actionBusy: boolean; returnTo: string
  onEdit(id: string): void; onDetails(id: string): void; onLink(row: GridRow): void; onAssignments(row: GridRow): void; onConfirm(row: GridRow): void; onRelease(row: GridRow): void
}) {
  const columns = useMemo<ColumnDef<GridRow>[]>(() => [
    { id: 'profile', header: 'Perfil', cell: ({ row: { original: r } }) => r.profileId ? <Link to={`/profiles/${r.profileId}`} state={{ returnTo }} className="font-medium text-link underline">{r.profileName}</Link> : null },
    { id: 'platforms', header: 'Plataformas', cell: ({ row: { original: r } }) => r.profileId && (r.platform1 || r.platform2) ? <div className="flex flex-wrap gap-1">{[r.platform1, r.platform2].filter(Boolean).map(name => <span key={name} className="inline-flex rounded-md bg-slate-100 px-2 py-0.5 text-xs text-slate-700">{name}</span>)}</div> : null },
    { accessorKey: 'host', header: 'Proxy', cell: ({ row: { original: r } }) => <div className="max-w-[240px] break-words"><span className="font-medium">{r.host}</span><span className="text-slate-500">:{r.port}</span><span className="block text-xs text-slate-500">{statusLabels[r.status]}</span></div> },
    { accessorKey: 'region', header: 'Região' },
    { accessorKey: 'expiresAt', header: 'Vencimento', cell: ({ row: { original: r } }) => <div className="whitespace-nowrap"><time dateTime={r.expiresAt}>{date.format(new Date(r.expiresAt))}</time>{Date.parse(r.expiresAt) <= Date.parse(evaluatedAt) && <span className="block text-xs text-red-700">Vencido</span>}</div> },
    { id: 'availability', header: 'Disponibilidade', cell: ({ row: { original: r } }) => <span className={`inline-flex whitespace-nowrap rounded-md px-2 py-0.5 text-xs ${r.availability === 'Available' ? 'bg-emerald-50 text-emerald-800' : r.availability === 'InUse' ? 'bg-sky-50 text-link' : 'bg-amber-50 text-amber-900'}`}>{availabilityLabels[r.availability]}</span> },
    { id: 'actions', header: 'Ações', cell: ({ row: { original: r } }) => <RowActions row={r} canWrite={canWrite} actionBusy={actionBusy} onEdit={onEdit} onDetails={onDetails} onLink={onLink} onAssignments={onAssignments} onConfirm={onConfirm} onRelease={onRelease} /> },
  ], [actionBusy, canWrite, evaluatedAt, onAssignments, onConfirm, onDetails, onEdit, onLink, onRelease, returnTo])
  const table = useReactTable({ data: items, columns, getCoreRowModel: getCoreRowModel(), manualFiltering: true, manualSorting: true, manualPagination: true,
    rowCount: total, getRowId: row => row.proxyId, state: { pagination: { pageIndex: options.page - 1, pageSize: options.pageSize }, sorting: [{ id: 'expiresAt', desc: options.direction === 'desc' }] } })
  return <div className="overflow-x-auto" role="region" aria-label="Grid operacional" tabIndex={0}><table className="w-full min-w-[1080px] text-left text-sm" aria-busy={busy}>
    <thead className="border-y border-slate-200 bg-slate-50 text-xs text-slate-600">{table.getHeaderGroups().map(group => <tr key={group.id}>{group.headers.map(header => <th key={header.id} scope="col" className="px-3 py-3 font-medium" aria-sort={header.id === 'expiresAt' ? options.direction === 'asc' ? 'ascending' : 'descending' : undefined}>{flexRender(header.column.columnDef.header, header.getContext())}</th>)}</tr>)}</thead>
    <tbody className="divide-y divide-slate-100">{table.getRowModel().rows.map(row => <tr key={row.id} className="hover:bg-slate-50">{row.getVisibleCells().map(cell => <td key={cell.id} className="px-3 py-2.5 align-top text-slate-700">{flexRender(cell.column.columnDef.cell, cell.getContext())}</td>)}</tr>)}</tbody>
  </table></div>
}
