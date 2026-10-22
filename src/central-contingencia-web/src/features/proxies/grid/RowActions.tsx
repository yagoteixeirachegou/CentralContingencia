import { useRef } from 'react'
import { CircleCheck, ChevronDown, Eye, Layers, Link2, Pencil, Unlink } from 'lucide-react'
import { Button } from '../../../components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from '../../../components/ui/dropdown-menu'
import type { GridRow } from './contracts'

interface Actions {
  canWrite: boolean; actionBusy: boolean
  onEdit(id: string): void; onDetails(id: string): void; onLink(row: GridRow): void; onAssignments(row: GridRow): void; onConfirm(row: GridRow): void; onRelease(row: GridRow): void
}

// Um único botão por linha. Só entram as operações possíveis para o estado da linha (a API continua decidindo a disponibilidade).
export function RowActions({ row, canWrite, actionBusy, onEdit, onDetails, onLink, onAssignments, onConfirm, onRelease }: Actions & { row: GridRow }) {
  const canLink = canWrite && !row.profileId && row.availability === 'Available'
  const canConfirm = canWrite && Boolean(row.allocationId) && row.availability === 'Reserved'
  const canRelease = canWrite && Boolean(row.allocationId) && (row.availability === 'Reserved' || row.availability === 'InUse')
  // Itens abrem diálogos: o menu não é modal e não devolve o foco ao botão depois de uma escolha, senão o foco do menu e o do diálogo disputam entre si.
  const chosen = useRef(false)
  const run = (action: () => void) => () => { chosen.current = true; action() }
  return <DropdownMenu modal={false}>
    <DropdownMenuTrigger asChild>
      <Button variant="outline" className="min-h-9 gap-2 px-3 py-1.5 text-sm" aria-label={`Ações de ${row.host}`}>Ações<ChevronDown size={16} aria-hidden="true" /></Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent onCloseAutoFocus={event => { if (chosen.current) { event.preventDefault(); chosen.current = false } }}>
      <DropdownMenuItem icon={<Eye size={18} />} onSelect={run(() => onDetails(row.proxyId))}>Detalhes</DropdownMenuItem>
      {canWrite && <DropdownMenuItem icon={<Pencil size={18} />} onSelect={run(() => onEdit(row.proxyId))}>Editar</DropdownMenuItem>}
      {canLink && <DropdownMenuItem icon={<Link2 size={18} />} onSelect={run(() => onLink(row))}>Vincular perfil</DropdownMenuItem>}
      {canWrite && row.profileId && <DropdownMenuItem icon={<Layers size={18} />} onSelect={run(() => onAssignments(row))}>Plataformas</DropdownMenuItem>}
      {canConfirm && <DropdownMenuItem icon={<CircleCheck size={18} />} disabled={actionBusy} onSelect={run(() => onConfirm(row))}>Confirmar reserva</DropdownMenuItem>}
      {canRelease && <><DropdownMenuSeparator /><DropdownMenuItem tone="danger" icon={<Unlink size={18} />} disabled={actionBusy} onSelect={run(() => onRelease(row))}>Liberar proxy</DropdownMenuItem></>}
    </DropdownMenuContent>
  </DropdownMenu>
}
