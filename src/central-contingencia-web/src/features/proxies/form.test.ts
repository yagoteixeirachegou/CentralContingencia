import { describe, expect, test } from 'vitest'
import { formDefaults, formPayload, proxySchema } from './form'
import type { ProxyItem } from './contracts'
const original: ProxyItem = { id: 'synthetic-id', host: 'proxy.example.test', port: 8080, usernameMasked: '***', status: 'Active', region: 'BR',
  expiresAt: '2000-01-01T12:34:56.789Z', createdAt: '1999-01-01T00:00:00Z', updatedAt: '1999-01-01T00:00:00Z', available: false }
const valid = { ...formDefaults(), host: 'proxy.example.test', port: '8080', username: 'synthetic-user', password: 'Synthetic!123', region: 'BR', expiresAt: '2099-01-01T12:00' }
describe('validação e semântica do contrato de proxies', () => {
  test.each(['proxy.example.test', '127.0.0.1', '2001:db8::1'])('aceita host %s', host => expect(proxySchema(false).safeParse({ ...valid, host }).success).toBe(true))
  test.each(['https://proxy.example.test', 'proxy.example.test:8080', '256.1.1.1', '127.00.0.1', '0x7f000001', '0x7f.1', '-proxy.test', 'a..test', ' proxy.test', '[::1]', 'fe80::1%eth0'])('recusa host %s', host => expect(proxySchema(false).safeParse({ ...valid, host }).success).toBe(false))
  test.each(['0', '65536', '1.5', '-1', 'abc'])('recusa porta %s', port => expect(proxySchema(false).safeParse({ ...valid, port }).success).toBe(false))
  test.each([{ username: ' ' }, { username: 'a'.repeat(129) }, { username: 'user\nname' }, { region: ' ' }, { region: 'a'.repeat(101) }, { status: 'Unknown' }, { password: '' }, { password: 'é'.repeat(513) }, { password: '\ud800' }, { expiresAt: '' }])('recusa campos inválidos %j', changes => expect(proxySchema(false).safeParse({ ...valid, ...changes }).success).toBe(false))
  test('edição não recupera credenciais e preserva vencimento passado com precisão', () => {
    const values = { ...formDefaults(original), status: 'Suspended' as const }
    expect(values.username).toBe(''); expect(values.password).toBe('')
    expect(proxySchema(true).safeParse(values).success).toBe(true)
    expect(formPayload(values, original)).toEqual({ status: 'Suspended' })
  })
  test('PATCH sem alteração é vazio e credenciais novas são explícitas', () => {
    expect(formPayload(formDefaults(original), original)).toEqual({})
    expect(formPayload({ ...formDefaults(original), username: 'replacement', password: 'Synthetic!456' }, original)).toEqual({ username: 'replacement', password: 'Synthetic!456' })
  })
  test('cadastro converte porta e vencimento para o contrato da API', () => {
    expect(formPayload(valid)).toMatchObject({ port: 8080, status: 'Disabled', expiresAt: new Date(valid.expiresAt).toISOString() })
  })
})
