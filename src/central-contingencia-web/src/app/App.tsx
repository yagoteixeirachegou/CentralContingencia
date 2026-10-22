import { BrowserRouter, Link, Route, Routes } from 'react-router-dom'
import { NotFound } from '../components/layout/StatusPages'
import { QueryScope } from './QueryScope'
import { lazy, Suspense } from 'react'
import { AuthProvider } from '../features/auth/AuthProvider'
import { useAuth } from '../features/auth/auth-context'
import { LoginPage } from '../features/auth/LoginPage'
import { ProtectedRoute } from '../features/auth/ProtectedRoute'
import { AuthenticatedLayout } from '../components/layout/AuthenticatedLayout'
import { Button } from '../components/ui/button'
import { PageSkeleton } from '../components/ui/skeleton'
const ProxiesPage = lazy(() => import('../features/proxies/ProxiesPage').then(module => ({ default: module.ProxiesPage })))
const ProxyImportPage = lazy(() => import('../features/proxies/import/ProxyImportPage').then(module => ({ default: module.ProxyImportPage })))
const IngestionPage = lazy(() => import('../features/ingestion/IngestionPage').then(module => ({ default: module.IngestionPage })))
const WarmupPage = lazy(() => import('../features/warmup/WarmupPage').then(module => ({ default: module.WarmupPage })))
const TasksPage = lazy(() => import('../features/tasks/TasksPage').then(module => ({ default: module.TasksPage })))
const ExecutorsPage = lazy(() => import('../features/tasks/ExecutorsPage').then(module => ({ default: module.ExecutorsPage })))
const DashboardPage = lazy(() => import('../features/dashboard/DashboardPage').then(module => ({ default: module.DashboardPage })))
const ProfilesPage = lazy(() => import('../features/profiles/ProfilesPage').then(module => ({ default: module.ProfilesPage })))
const ProfileDetailsPage = lazy(() => import('../features/profiles/ProfileDetailsPage').then(module => ({ default: module.ProfileDetailsPage })))
function SessionRoutes() {
  const auth = useAuth()
  if (auth.loading) return <main className="p-8" role="status">Verificando sessão…</main>
  if (auth.error) return <main className="p-8"><p role="alert">{auth.error}</p><Button onClick={() => void auth.refresh()}>Tentar novamente</Button></main>
  return <Routes>
    <Route path="/login" element={<LoginPage />} />
    <Route element={<ProtectedRoute />}>
      <Route element={<AuthenticatedLayout />}>
        <Route index element={<section className="rounded-2xl border border-slate-200 bg-surface p-8"><h1 className="text-3xl font-semibold">Início</h1><p className="mt-4 text-slate-600">Gestão da infraestrutura de contingência.</p><Link className="mt-6 inline-flex rounded-lg bg-sky-800 px-5 py-3 text-sm font-medium text-white hover:bg-sky-900" to="/proxies">Abrir HTTP - FACEBOOK</Link></section>} />
        <Route path="/dashboard" element={<Suspense fallback={<PageSkeleton label="Carregando dashboard…" />}><DashboardPage /></Suspense>} />
        <Route path="/tasks" element={<Suspense fallback={<PageSkeleton label="Carregando tarefas…" />}><TasksPage /></Suspense>} />
        <Route path="/tasks/executors" element={<Suspense fallback={<PageSkeleton label="Carregando executores…" />}><ExecutorsPage /></Suspense>} />
        <Route path="/proxies" element={<Suspense fallback={<PageSkeleton label="Carregando gestão de proxies…" />}><ProxiesPage /></Suspense>} />
        <Route path="/proxies/ingestion" element={<Suspense fallback={<PageSkeleton label="Carregando envio de perfis…" />}><IngestionPage /></Suspense>} />
        <Route path="/warmup" element={<Suspense fallback={<PageSkeleton label="Carregando aquecimento…" />}><WarmupPage /></Suspense>} />
        <Route path="/profiles" element={<Suspense fallback={<PageSkeleton label="Carregando perfis…" />}><ProfilesPage /></Suspense>} />
        <Route path="/profiles/:id" element={<Suspense fallback={<PageSkeleton label="Carregando perfil…" />}><ProfileDetailsPage /></Suspense>} />
        <Route element={<ProtectedRoute role="Admin" />}>
          <Route path="/proxies/import" element={<Suspense fallback={<PageSkeleton label="Carregando importação…" />}><ProxyImportPage /></Suspense>} />
          <Route path="/administration" element={<section><h1 className="text-2xl font-bold">Administração</h1><p className="mt-4">Criação de usuários disponível exclusivamente para Admin pela API documentada.</p></section>} />
        </Route>
        <Route path="*" element={<NotFound />} />
      </Route>
    </Route>
  </Routes>
}
export function App() {
  return <BrowserRouter><AuthProvider><QueryScope><SessionRoutes /></QueryScope></AuthProvider></BrowserRouter>
}
