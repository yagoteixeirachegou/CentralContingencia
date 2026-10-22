import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { AccessDenied } from '../../components/layout/StatusPages'
import { sessionEndedNotice, useAuth } from './auth-context'
export function ProtectedRoute({ role }: { role?: string }) {
  const { user, notice } = useAuth()
  const location = useLocation()
  // Guarda a página de origem para voltar a ela após o login; saída voluntária não guarda.
  if (!user) return <Navigate to="/login" replace state={notice === sessionEndedNotice ? undefined : { from: location.pathname + location.search }} />
  if (role && !user.roles.includes(role)) return <AccessDenied reason="Seu usuário não tem permissão para acessar esta página." />
  return <Outlet />
}
