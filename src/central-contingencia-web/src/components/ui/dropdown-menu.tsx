import * as Primitive from '@radix-ui/react-dropdown-menu'
import type { ComponentProps, ReactNode } from 'react'
import { cn } from '../../lib/utils'

export const DropdownMenu = Primitive.Root
export const DropdownMenuTrigger = Primitive.Trigger

export function DropdownMenuContent({ className, children, ...props }: ComponentProps<typeof Primitive.Content>) {
  return <Primitive.Portal>
    <Primitive.Content align="end" sideOffset={6} className={cn('z-50 min-w-[220px] rounded-xl border border-slate-200 bg-surface p-1.5 shadow-xl', className)} {...props}>{children}</Primitive.Content>
  </Primitive.Portal>
}

/** Item com ícone e texto. `tone="danger"` destaca ações destrutivas (Von Restorff). */
export function DropdownMenuItem({ icon, tone, className, children, ...props }: ComponentProps<typeof Primitive.Item> & { icon: ReactNode; tone?: 'danger' }) {
  return <Primitive.Item className={cn('flex min-h-10 cursor-pointer select-none items-center gap-3 rounded-lg px-3 py-2 text-sm outline-none data-[disabled]:cursor-not-allowed data-[disabled]:opacity-50 data-[highlighted]:bg-slate-100',
    tone === 'danger' ? 'text-red-800' : 'text-slate-800', className)} {...props}>
    <span aria-hidden="true" className="shrink-0">{icon}</span>{children}
  </Primitive.Item>
}

export const DropdownMenuSeparator = ({ className }: { className?: string }) => <Primitive.Separator className={cn('my-1 h-px bg-slate-200', className)} />
