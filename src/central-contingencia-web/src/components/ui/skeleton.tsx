import type { ReactNode } from 'react'
import { cn } from '../../lib/utils'

/** Bloco que imita o conteúdo que vai chegar. Decorativo: quem informa o carregamento é o rótulo de <Loading>. */
export function Skeleton({ className }: { className?: string }) {
  return <div aria-hidden="true" className={cn('animate-pulse rounded-md bg-slate-200 motion-reduce:animate-none', className)} />
}

/** Região de carregamento acessível: leitores de tela ouvem o rótulo; a tela mostra a silhueta do conteúdo. */
export function Loading({ label, className, children }: { label: string; className?: string; children: ReactNode }) {
  return <div role="status" aria-busy="true" className={className}><span className="sr-only">{label}</span>{children}</div>
}

export function TableSkeleton({ label, columns = 6, rows = 6, className }: { label: string; columns?: number; rows?: number; className?: string }) {
  return <Loading label={label} className={cn('p-4', className)}>
    <div className="grid gap-3" style={{ gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` }}>
      {Array.from({ length: columns }, (_, column) => <Skeleton key={`h${column}`} className="h-4 w-2/3" />)}
      {Array.from({ length: rows * columns }, (_, cell) => <Skeleton key={cell} className={cn('h-5', cell % columns === 0 ? 'w-full' : 'w-4/5')} />)}
    </div>
  </Loading>
}

export function CardsSkeleton({ label, cards = 4, className }: { label: string; cards?: number; className?: string }) {
  return <Loading label={label} className={cn('grid gap-4 sm:grid-cols-2 lg:grid-cols-4', className)}>
    {Array.from({ length: cards }, (_, index) => <div key={index} className="space-y-3 rounded-2xl border border-slate-200 bg-surface p-5"><Skeleton className="h-3 w-1/3" /><Skeleton className="h-7 w-2/3" /></div>)}
  </Loading>
}

export function ListSkeleton({ label, rows = 4, className }: { label: string; rows?: number; className?: string }) {
  return <Loading label={label} className={cn('space-y-3', className)}>
    {Array.from({ length: rows }, (_, index) => <Skeleton key={index} className="h-9 w-full" />)}
  </Loading>
}

export function PageSkeleton({ label }: { label: string }) {
  return <Loading label={label} className="space-y-6">
    <div className="space-y-3"><Skeleton className="h-9 w-1/3" /><Skeleton className="h-4 w-1/2" /></div>
    <div className="space-y-3 rounded-2xl border border-slate-200 bg-surface p-5"><Skeleton className="h-5 w-1/4" /><Skeleton className="h-4 w-full" /><Skeleton className="h-4 w-5/6" /><Skeleton className="h-4 w-2/3" /></div>
  </Loading>
}
