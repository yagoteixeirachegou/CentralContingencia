export class ApiError extends Error {
  constructor(public status: number, message: string, public problem?: unknown) { super(message) }
}
const offlineMessage = 'Sem conexão com o servidor. Verifique a rede e tente novamente.'
// Falhas de rede chegam como TypeError ("Failed to fetch"); cancelamentos (AbortError) seguem intactos.
async function request(input: string, init: RequestInit): Promise<Response> {
  try { return await fetch(input, init) }
  catch (reason) {
    if (reason instanceof DOMException && reason.name === 'AbortError') throw reason
    throw new ApiError(0, offlineMessage)
  }
}
export async function api<T>(path: string, body?: unknown, options: { method?: 'POST' | 'PATCH'; signal?: AbortSignal; headers?: Record<string, string>; readProblem?: boolean } = {}): Promise<T> {
  const headers: Record<string, string> = { ...options.headers }
  const multipart = body instanceof FormData
  if (body !== undefined) {
    const csrf = await request('/api/v1/auth/csrf', { credentials: 'same-origin', cache: 'no-store', signal: options.signal })
    if (!csrf.ok) throw new ApiError(csrf.status, 'Não foi possível preparar a requisição. Atualize a página.')
    const token: { token: string } = await csrf.json()
    headers['X-CSRF-TOKEN'] = token.token
    if (!multipart) headers['Content-Type'] = 'application/json'
  }
  const response = await request('/api/v1' + path, {
    method: body === undefined ? 'GET' : options.method ?? 'POST',
    signal: options.signal,
    credentials: 'same-origin', cache: 'no-store', headers,
    body: body === undefined ? undefined : multipart ? body : JSON.stringify(body),
  })
  if (!response.ok) {
    if (response.status === 401 && path !== '/auth/login' && path !== '/auth/me')
      window.dispatchEvent(new Event('session-expired'))
    const loginAttempt = path === '/auth/login'
    const message = response.status === 401 ? loginAttempt ? 'Credenciais inválidas ou acesso temporariamente bloqueado.' : 'Sua sessão expirou. Entre novamente.'
      : response.status === 403 ? 'Acesso negado. Seu usuário não tem permissão.'
      : response.status === 429 ? 'Muitas tentativas. Aguarde um minuto e tente novamente.'
      : response.status === 400 ? 'Requisição inválida. Atualize a página e verifique os campos.'
      : 'Não foi possível concluir a solicitação. Tente novamente.'
    const problem: unknown = options.readProblem ? await response.json().catch(() => undefined) : undefined
    throw new ApiError(response.status, message, problem)
  }
  if (response.status === 204) return undefined as T
  const text = await response.text()
  return text ? JSON.parse(text) as T : undefined as T
}
