import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { Button } from '../../components/ui/button'
import { ApiError } from '../../lib/api/client'
import { createProxy, updateProxy, proxyError } from './api'
import { formDefaults, formPayload, localDate, proxySchema, type ProxyFormValues } from './form'
import { statusLabels, type ProxyInput, type ProxyItem, type ProxyPatch } from './contracts'
export function ProxyForm({ original, onSaved, onCancel, onBusy }: {
  original?: ProxyItem; onSaved(): void; onCancel(): void; onBusy(busy: boolean): void
}) {
  const [error, setError] = useState('')
  const client = useQueryClient()
  const { register, handleSubmit, resetField, setError: fieldError, formState: { errors, isSubmitting } } = useForm<ProxyFormValues>({
    resolver: zodResolver(proxySchema(Boolean(original))), defaultValues: formDefaults(original),
  })
  const mutation = useMutation({ mutationFn: (input: ProxyInput | ProxyPatch) => original
    ? updateProxy(original.id, input) : createProxy(input as ProxyInput), retry: false, gcTime: 0 })
  async function submit(values: ProxyFormValues) {
    setError('')
    const changedExpiry = !original || values.expiresAt !== localDate(original.expiresAt)
    if (changedExpiry && new Date(values.expiresAt).getTime() <= Date.now()) {
      fieldError('expiresAt', { message: 'O novo vencimento deve estar no futuro.' }); return
    }
    const payload = formPayload(values, original)
    if (Object.keys(payload).length === 0) { setError('Altere pelo menos um campo para salvar.'); return }
    onBusy(true)
    try {
      await mutation.mutateAsync(payload)
      void client.invalidateQueries({ queryKey: ['proxies'] })
      onSaved()
    } catch (reason) {
      setError(reason instanceof ApiError ? proxyError(reason) : 'Não foi possível confirmar a gravação. Atualize a lista antes de tentar novamente.')
    } finally {
      resetField('password'); resetField('username')
      if ('password' in payload) payload.password = ''
      if ('username' in payload) payload.username = ''
      values.password = ''; values.username = ''
      mutation.reset(); onBusy(false)
    }
  }
  const field = (name: keyof ProxyFormValues, label: string, type = 'text', hint?: string) => <div>
    <label htmlFor={`proxy-${name}`} className="mb-1.5 block text-sm font-medium text-slate-800">{label}</label>
    <Input id={`proxy-${name}`} type={type} {...register(name)} aria-invalid={Boolean(errors[name])}
      aria-describedby={errors[name] ? `proxy-${name}-error` : hint ? `proxy-${name}-hint` : undefined}
      inputMode={name === 'port' ? 'numeric' : undefined} maxLength={{ host: 253, port: 5, region: 100, username: 128, password: 1024, status: undefined, expiresAt: undefined }[name]}
      autoComplete={name === 'password' ? 'new-password' : 'off'} />
    {hint && <p id={`proxy-${name}-hint`} className="mt-1 text-xs text-slate-500">{hint}</p>}
    {errors[name] && <p id={`proxy-${name}-error`} role="alert" className="mt-1 text-sm text-red-700">{errors[name]?.message}</p>}
  </div>
  return <form onSubmit={handleSubmit(submit)} className="mt-6 space-y-5" noValidate>
    <fieldset disabled={isSubmitting} className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-[2fr_1fr]">{field('host', 'Host', 'text', 'DNS, IPv4 ou IPv6. Sem protocolo ou porta.')}{field('port', 'Porta', 'text', '1 a 65535')}</div>
      <div className="grid gap-4 sm:grid-cols-2">{field('region', 'Região')}
        <div><label htmlFor="proxy-status" className="mb-1.5 block text-sm font-medium">Status operacional</label>
          <Select id="proxy-status" {...register('status')}>{Object.entries(statusLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</Select></div>
      </div>
      {field('expiresAt', 'Vencimento', 'datetime-local', 'Data e hora locais. Um novo vencimento deve estar no futuro.')}
      <div className="rounded-xl border border-slate-200 bg-slate-50 p-4">
        <p className="mb-3 text-sm font-semibold">Credenciais de conexão</p>
        {original && <p className="mb-3 text-xs text-slate-600">Usuário atual: {original.usernameMasked}. Deixe os campos em branco para preservar as credenciais.</p>}
        <div className="grid gap-4 sm:grid-cols-2">{field('username', original ? 'Novo usuário (opcional)' : 'Usuário')}{field('password', original ? 'Nova senha (opcional)' : 'Senha do proxy', 'password')}</div>
        <p className="mt-3 text-xs text-slate-500">A senha salva nunca é exibida. Credenciais ficam somente na memória durante o envio.</p>
      </div>
    </fieldset>
    {error && <p role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-800">{error}</p>}
    <div className="flex justify-end gap-3 border-t border-slate-100 pt-4">
      <Button type="button" variant="outline" disabled={isSubmitting} onClick={onCancel}>Cancelar</Button>
      <Button type="submit" disabled={isSubmitting}>{isSubmitting ? 'Salvando…' : original ? 'Salvar alterações' : 'Cadastrar proxy'}</Button>
    </div>
  </form>
}
