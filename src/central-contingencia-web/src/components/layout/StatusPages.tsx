import { Link } from 'react-router-dom'
import { Compass, Home, ShieldAlert } from 'lucide-react'
import { useAuth } from '../../features/auth/auth-context'

const home = 'inline-flex items-center gap-2 rounded-lg bg-sky-800 px-4 py-2.5 text-sm font-medium text-white hover:bg-sky-900 focus-visible:outline-2 focus-visible:outline-offset-2'

// Estado de permissão negada: diz o motivo, mostra o papel atual e oferece saída.
export function AccessDenied({ reason }: { reason: string }) {
  const { user } = useAuth()
  return <section className="mx-auto max-w-lg rounded-2xl border border-slate-200 bg-surface p-8 text-center">
    <ShieldAlert size={36} className="mx-auto text-amber-700" aria-hidden="true" />
    <h1 className="mt-4 text-2xl font-semibold text-slate-950">Acesso negado</h1>
    <p role="alert" className="mt-2 text-slate-700">{reason}</p>
    {user && <p className="mt-2 text-sm text-slate-600">Seu papel atual: {user.roles.join(' · ') || 'nenhum'}. Se você precisa deste acesso, peça a um administrador.</p>}
    <Link to="/" className={`${home} mt-6`}><Home size={16} aria-hidden="true" />Voltar ao início</Link>
  </section>
}

// Rota inexistente: mantém o menu (quando há sessão) e oferece caminhos de volta.
export function NotFound() {
  return <section className="mx-auto max-w-lg rounded-2xl border border-slate-200 bg-surface p-8 text-center">
    <Compass size={36} className="mx-auto text-slate-500" aria-hidden="true" />
    <h1 className="mt-4 text-2xl font-semibold text-slate-950">Página não encontrada</h1>
    <p className="mt-2 text-slate-700">O endereço não existe ou foi movido. Confira o link ou volte para uma área conhecida.</p>
    <div className="mt-6 flex flex-wrap justify-center gap-3">
      <Link to="/" className={home}><Home size={16} aria-hidden="true" />Ir para o início</Link>
      <Link to="/proxies" className="inline-flex items-center rounded-md border border-slate-300 bg-surface px-4 py-2.5 text-sm text-link hover:bg-slate-100">Abrir HTTP - FACEBOOK</Link>
    </div>
  </section>
}
