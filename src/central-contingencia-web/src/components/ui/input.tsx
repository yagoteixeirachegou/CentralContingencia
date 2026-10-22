import type { ComponentProps } from 'react'
import { cn } from '../../lib/utils'
export function Input({ className, ...props }: ComponentProps<'input'>) {
  return <input className={cn('w-full rounded-md border border-slate-300 bg-surface px-3 py-2 focus-visible:outline-2 focus-visible:outline-sky-700 disabled:opacity-50', className)} {...props} />
}
