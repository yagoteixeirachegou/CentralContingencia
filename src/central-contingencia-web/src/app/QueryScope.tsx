import { useEffect, useState, type ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useAuth } from '../features/auth/auth-context'
export function QueryScope({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const sessionKey = user ? `${user.id}:${[...user.roles].sort().join(',')}` : 'anonymous'
  return <SessionQueries key={sessionKey}>{children}</SessionQueries>
}
function SessionQueries({ children }: { children: ReactNode }) {
  const [client] = useState(() => new QueryClient({ defaultOptions: {
    queries: { retry: false, staleTime: 20_000 }, mutations: { retry: false, gcTime: 0 },
  } }))
  useEffect(() => () => { void client.cancelQueries(); client.clear() }, [client])
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}
