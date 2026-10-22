import { useState } from 'react'
import { brand } from '../../lib/brand'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { Navigate, useLocation } from 'react-router-dom'
import { Activity, Eye, EyeOff } from 'lucide-react'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { ThemeToggle } from '../../components/ui/theme-toggle'
import { LoginArt } from './LoginArt'
import { useAuth } from './auth-context'

const schema = z.object({
  email: z.email('Informe um e-mail válido.').max(254),
  password: z.string().min(1, 'Informe a senha.').max(128),
})
type LoginForm = z.infer<typeof schema>
export function LoginPage() {
  const auth = useAuth()
  const [message, setMessage] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const { register, handleSubmit, resetField, formState: { errors, isSubmitting } } = useForm<LoginForm>({ resolver: zodResolver(schema) })
  const from = (useLocation().state as { from?: unknown } | null)?.from
  // Só caminhos internos: evita redirecionamento para fora do site.
  const destination = typeof from === 'string' && /^\/(?![/\\])/.test(from) && !from.startsWith('/login') ? from : '/'
  if (auth.user) return <Navigate to={destination} replace />
  async function submit(data: LoginForm) {
    setMessage('')
    try { await auth.login(data.email, data.password) }
    catch (reason) { setMessage(reason instanceof Error ? reason.message : 'Não foi possível entrar.') }
    finally { resetField('password') }
  }
  const label = 'mb-1.5 block text-sm font-medium text-slate-700'
  const fieldError = 'mt-1.5 text-sm text-red-700'
  return <main className="grid min-h-dvh bg-slate-50 lg:grid-cols-[minmax(0,1.05fr)_minmax(0,1fr)]">
    <section className="flex flex-col gap-10 bg-ink px-6 py-8 text-white sm:px-10 lg:px-14 lg:py-12">
      <div className="flex items-center gap-3"><span className="rounded-xl bg-sky-800 p-2.5"><Activity size={22} aria-hidden="true" /></span><p className="text-sm font-semibold leading-5">{brand.first}<br />{brand.second}</p></div>
      <div className="max-w-xl lg:mt-auto">
        <LoginArt className="hidden w-full max-w-[460px] sm:block" />
        <p className="mt-2 text-3xl font-semibold leading-tight tracking-tight sm:mt-8 lg:text-4xl">Cada perfil com a sua proxy.</p>
        <p className="mt-3 max-w-md text-base leading-relaxed text-white/70">Reserva, vínculo e liberação da operação em um só lugar, com o histórico de cada alteração.</p>
      </div>
    </section>
    <section className="relative flex items-center justify-center border-slate-200 px-6 py-12 sm:px-10 lg:border-l">
      <ThemeToggle className="absolute right-4 top-4 gap-2 px-3 text-sm sm:right-6 sm:top-6" />
      <div className="w-full max-w-sm">
        <Badge>Ambiente de desenvolvimento</Badge>
        <h1 className="mt-5 text-3xl font-semibold tracking-tight text-slate-950">Acesso interno</h1>
        <p className="mt-2 text-slate-600">Use a conta criada por um administrador.</p>
        {auth.notice && <p role="status" className="mt-5 rounded-lg border border-sky-200 bg-sky-50 px-3 py-2.5 text-sm text-link">{auth.notice}</p>}
        {message && <p role="alert" className="mt-5 rounded-lg border border-red-200 bg-red-50 px-3 py-2.5 text-sm text-red-800">{message}</p>}
        <form onSubmit={handleSubmit(submit)} className="mt-6 space-y-5" noValidate>
          <div><label htmlFor="email" className={label}>E-mail</label><Input id="email" type="email" autoComplete="username" className="py-2.5" aria-invalid={!!errors.email} aria-describedby={errors.email ? 'email-error' : undefined} {...register('email')} />
            {errors.email && <p id="email-error" role="alert" className={fieldError}>{errors.email.message}</p>}</div>
          <div><label htmlFor="password" className={label}>Senha</label><div className="relative"><Input id="password" type={showPassword ? 'text' : 'password'} autoComplete="current-password" className="py-2.5 pr-11" aria-invalid={!!errors.password} aria-describedby={errors.password ? 'password-error' : undefined} {...register('password')} /><button type="button" aria-pressed={showPassword} aria-label={showPassword ? 'Ocultar senha' : 'Mostrar senha'} onClick={() => setShowPassword(value => !value)} className="absolute inset-y-0 right-0 flex w-11 items-center justify-center rounded-md text-slate-500 hover:text-slate-800 focus-visible:outline-2 focus-visible:outline-sky-700">{showPassword ? <EyeOff size={18} aria-hidden="true" /> : <Eye size={18} aria-hidden="true" />}</button></div>
            {errors.password && <p id="password-error" role="alert" className={fieldError}>{errors.password.message}</p>}</div>
          <Button type="submit" className="w-full py-2.5" disabled={isSubmitting}>{isSubmitting ? 'Entrando…' : 'Entrar'}</Button>
        </form>
        <p className="mt-8 text-sm text-slate-500">Não há cadastro público. O acesso é exclusivo de contas internas.</p>
      </div>
    </section>
  </main>
}
