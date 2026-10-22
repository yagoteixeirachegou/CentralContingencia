import type { ComponentProps } from 'react'
import { cva, type VariantProps } from 'class-variance-authority'
import { cn } from '../../lib/utils'
const variants = cva('inline-flex items-center justify-center rounded-md px-4 py-2 font-medium focus-visible:outline-2 focus-visible:outline-offset-2 disabled:cursor-not-allowed disabled:opacity-50', {
  variants: { variant: { default: 'bg-sky-800 text-white hover:bg-sky-900', outline: 'border border-slate-300 bg-surface text-slate-800 hover:bg-slate-100' } },
  defaultVariants: { variant: 'default' },
})
export function Button({ className, variant, ...props }: ComponentProps<'button'> & VariantProps<typeof variants>) {
  return <button className={cn(variants({ variant }), className)} {...props} />
}
