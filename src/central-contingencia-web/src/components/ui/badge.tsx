import type { ComponentProps } from 'react'
import { cva, type VariantProps } from 'class-variance-authority'
import { cn } from '../../lib/utils'
const variants = cva('inline-flex rounded-full border px-3 py-1 text-xs font-semibold', {
  variants: { variant: { default: 'border-sky-200 bg-sky-50 text-link' } },
  defaultVariants: { variant: 'default' },
})
export function Badge({ className, variant, ...props }: ComponentProps<'span'> & VariantProps<typeof variants>) {
  return <span className={cn(variants({ variant }), className)} {...props} />
}
