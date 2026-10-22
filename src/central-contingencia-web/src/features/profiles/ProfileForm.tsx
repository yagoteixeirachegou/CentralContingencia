import { useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Input } from '../../components/ui/input'
import { Button } from '../../components/ui/button'
import { Select } from '../../components/ui/select'
import { assignments } from '../proxies/grid/contracts'
import { createProfile, operationalError, refreshOperations } from './api'
import { ApiError } from '../../lib/api/client'
const schema = z.object({ name: z.string().trim().min(1, 'Informe o nome.').max(200, 'Use até 200 caracteres.'), platform: z.string().trim().max(100, 'Use até 100 caracteres.'), platform1: z.enum([...assignments, '']), platform2: z.enum([...assignments, '']) })
export function ProfileForm({ onCreated, onCancel, onBusy }: { onCreated(id: string): void; onCancel(): void; onBusy(busy: boolean): void }) {
  const client = useQueryClient()
  const guard = useRef(false)
  const [error, setError] = useState('')
  const [uncertain, setUncertain] = useState(false)
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<z.infer<typeof schema>>({ resolver: zodResolver(schema), defaultValues: { name: '', platform: '', platform1: '', platform2: '' } })
  const mutation = useMutation({ mutationFn: createProfile, retry: false })
  return <form className="mt-6 space-y-4" noValidate onSubmit={handleSubmit(async values => {
    if (guard.current || uncertain) return
    guard.current = true; onBusy(true); setError('')
    try { const profile = await mutation.mutateAsync({ name: values.name, ...(values.platform ? { platform: values.platform } : {}), ...(values.platform1 ? { platform1: values.platform1 } : {}), ...(values.platform2 ? { platform2: values.platform2 } : {}) }); await refreshOperations(client); onCreated(profile.id) }
    catch (reason) { setError(operationalError(reason)); setUncertain(!(reason instanceof ApiError) || reason.status >= 500); await refreshOperations(client) }
    finally { guard.current = false; onBusy(false) }
  })}>
    <fieldset disabled={isSubmitting || uncertain} className="space-y-4">
      {(['name', 'platform'] as const).map(name => <div key={name}><label htmlFor={`profile-${name}`} className="mb-1.5 block text-sm font-medium">{name === 'name' ? 'Nome do perfil' : 'Plataforma'}</label><Input id={`profile-${name}`} {...register(name)} maxLength={name === 'name' ? 200 : 100} aria-invalid={Boolean(errors[name])} aria-describedby={errors[name] ? `profile-error-${name}` : undefined} />{errors[name] && <p id={`profile-error-${name}`} role="alert" className="mt-1 text-sm text-red-700">{errors[name]?.message}</p>}</div>)}
      <p className="text-xs text-slate-500">Plataforma textual anterior (opcional). As atribuições abaixo são independentes desse campo.</p>
      <div className="grid gap-3 sm:grid-cols-2">{(['platform1', 'platform2'] as const).map((name, index) => <div key={name}><label htmlFor={`profile-${name}`} className="mb-1 block text-sm">Plataforma {index + 1}</label><Select id={`profile-${name}`} {...register(name)}><option value="">Sem atribuição</option>{assignments.map(value => <option key={value} value={value}>{value}</option>)}</Select></div>)}</div>
    </fieldset>
    {error && <p role="alert" className="text-sm text-red-700">{error}</p>}
    {uncertain && <p className="text-sm text-slate-600">Feche o formulário e confira a listagem antes de iniciar outro cadastro.</p>}
    <div className="flex justify-end gap-3"><Button type="button" variant="outline" disabled={isSubmitting} onClick={onCancel}>Cancelar</Button><Button disabled={isSubmitting || uncertain}>{isSubmitting ? 'Cadastrando…' : 'Cadastrar perfil'}</Button></div>
  </form>
}
