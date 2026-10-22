import * as Primitive from '@radix-ui/react-dialog'
import { X } from 'lucide-react'
import { useRef, type ReactNode } from 'react'
export function Dialog({ open, onOpenChange, title, description, children, busy = false }: {
  open: boolean; onOpenChange(open: boolean): void; title: string; description: string; children: ReactNode; busy?: boolean
}) {
  const returnFocus = useRef<HTMLElement | null>(null)
  return <Primitive.Root open={open} onOpenChange={value => { if (!busy) onOpenChange(value) }}>
    <Primitive.Portal>
      <Primitive.Overlay className="fixed inset-0 z-40 bg-black/55 backdrop-blur-sm" />
      <Primitive.Content onOpenAutoFocus={() => { returnFocus.current = document.activeElement instanceof HTMLElement ? document.activeElement : null }}
        onCloseAutoFocus={event => { event.preventDefault(); returnFocus.current?.focus() }}
        className="fixed left-1/2 top-1/2 z-50 max-h-[90dvh] w-[calc(100%_-_2rem)] max-w-xl -translate-x-1/2 -translate-y-1/2 overflow-y-auto rounded-2xl bg-surface p-6 shadow-2xl">
        <Primitive.Title className="pr-8 text-xl font-semibold text-slate-950">{title}</Primitive.Title>
        <Primitive.Description className="mt-2 text-sm text-slate-600">{description}</Primitive.Description>
        <Primitive.Close disabled={busy} className="absolute right-5 top-5 rounded-md p-1 text-slate-500 hover:bg-slate-100 focus-visible:outline-2 disabled:opacity-50" aria-label="Fechar formulário"><X size={20} /></Primitive.Close>
        {children}
      </Primitive.Content>
    </Primitive.Portal>
  </Primitive.Root>
}
