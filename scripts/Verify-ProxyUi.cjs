// Chrome real; mocks confinados ao Playwright ou proxy Vite exclusivo para a API descartável.
const { chromium, request: playwrightRequest } = require('../src/central-contingencia-web/node_modules/playwright-core')
const assert = require('node:assert/strict')
const fs = require('node:fs')
const path = require('node:path')
const { randomUUID } = require('node:crypto')
const { pathToFileURL } = require('node:url')
const root = path.resolve(__dirname, '..')
const artifacts = path.join(root, '.local/verification-2.2b')
const real = process.argv.includes('--real')
const origin = real ? 'https://localhost:5174' : 'https://localhost:5173'
const apiOrigin = process.env.CENTRAL_UI_API_ORIGIN
const mode = real ? 'api-descartavel' : 'mocks'
let stage = 'configuração'
let browser
let isolatedVite
let pageErrors = 0
let unreadResponses = 0
let sensitiveResponses = 0
const checks = []
const responseStatuses = []
const secret = 'SyntheticProxy!123'
const username = 'synthetic-proxy-user'
const check = name => {
  checks.push(name); stage = name
  fs.writeFileSync(path.join(artifacts, `${mode}-progress.json`), JSON.stringify({ mode, completedChecks: checks }, null, 2))
}
const screenshot = (page, name) => page.screenshot({ path: path.join(artifacts, `${mode}-${name}.png`), fullPage: !name.includes('cadastro') && !name.includes('edicao') })
async function label(page) {
  await page.evaluate(labelText => {
    const element = document.createElement('div'); element.id = 'browser-evidence-label'; element.textContent = labelText
    element.style.cssText = 'position:fixed;top:0;left:0;right:0;z-index:9999;padding:5px 12px;background:#fef3c7;color:#78350f;font:12px system-ui;text-align:center;pointer-events:none'
    document.body.prepend(element); document.body.style.paddingTop = '28px'
  }, real ? 'VALIDAÇÃO LOCAL — API e PostgreSQL descartáveis; dados sintéticos' : 'VALIDAÇÃO LOCAL — respostas HTTP simuladas; dados sintéticos')
}
function mockRows() {
  return Array.from({ length: 32 }, (_, i) => ({ id: randomUUID(), host: `proxy-${String(i + 1).padStart(2, '0')}.example.test`, port: 8000 + i, usernameMasked: '***',
    region: i % 3 ? 'BR / São Paulo' : 'BR / Recife', status: i % 4 === 0 ? 'Suspended' : 'Active',
    expiresAt: i % 7 === 0 ? '2020-01-01T12:00:00Z' : '2099-01-01T12:00:00Z', createdAt: '2026-10-11T01:00:00Z', updatedAt: '2026-10-11T01:00:00Z',
    available: i % 4 !== 0 && i % 7 !== 0 }))
}
async function mockedContext(role = 'Admin', viewport = { width: 1440, height: 1050 }) {
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo' })
  const state = { rows: mockRows(), session: true, fail: false, writes: 0, lastPatch: null }
  await context.route(url => url.pathname.startsWith('/api/'), async route => {
    const request = route.request(); const url = new URL(request.url()); const method = request.method()
    const reply = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: status === 204 ? '' : JSON.stringify(body) })
    if (url.pathname.endsWith('/csrf')) return reply({ token: 'browser-synthetic-csrf' })
    if (url.pathname.endsWith('/me')) return state.session ? reply({ id: `synthetic-${role}`, email: `${role.toLowerCase()}@example.test`, roles: [role] }) : reply({}, 401)
    if (method !== 'GET') {
      assert.equal(request.headers()['x-csrf-token'], 'browser-synthetic-csrf')
      if (url.pathname.endsWith('/logout')) { state.session = false; return reply({}, 204) }
      if (role === 'Operator') return reply({}, 403)
      const body = request.postDataJSON(); state.writes++
      if (method === 'POST') {
        const row = { id: randomUUID(), host: body.host, port: body.port, usernameMasked: '***', status: body.status, region: body.region, expiresAt: body.expiresAt,
          createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(), available: body.status === 'Active' && new Date(body.expiresAt) > new Date() }
        state.rows.unshift(row); return reply(row, 201)
      }
      state.lastPatch = body
      const index = state.rows.findIndex(row => url.pathname.endsWith(row.id)); assert.ok(index >= 0)
      for (const field of ['host', 'port', 'region', 'expiresAt', 'status']) if (field in body) state.rows[index][field] = body[field]
      state.rows[index].updatedAt = new Date().toISOString(); state.rows[index].available = state.rows[index].status === 'Active' && new Date(state.rows[index].expiresAt) > new Date()
      return reply(state.rows[index])
    }
    if (state.fail) return reply({}, 503)
    const detail = state.rows.find(row => url.pathname.endsWith(row.id)); if (detail) return reply(detail)
    let rows = state.rows.filter(row => (!url.pathname.endsWith('/available') || row.available) &&
      (!url.searchParams.has('host') || row.host === url.searchParams.get('host')) && (!url.searchParams.has('port') || row.port === Number(url.searchParams.get('port'))) &&
      (!url.searchParams.has('region') || row.region === url.searchParams.get('region')) && (!url.searchParams.has('status') || row.status === url.searchParams.get('status')) &&
      (!url.searchParams.has('expiresBefore') || new Date(row.expiresAt) <= new Date(url.searchParams.get('expiresBefore'))))
    const field = url.searchParams.get('sort') || 'createdAt'; const direction = url.searchParams.get('direction') === 'asc' ? 1 : -1
    rows = rows.sort((a, b) => (a[field] < b[field] ? -1 : a[field] > b[field] ? 1 : a.id.localeCompare(b.id)) * direction)
    const page = Number(url.searchParams.get('page') || 1), pageSize = Number(url.searchParams.get('pageSize') || 20)
    return reply({ items: rows.slice((page - 1) * pageSize, page * pageSize), page, pageSize, total: rows.length })
  })
  const page = await context.newPage(); page.on('pageerror', () => { pageErrors++ })
  return { context, page, state }
}
async function create(page, host) {
  await page.getByRole('button', { name: 'Novo proxy', exact: true }).click()
  const modal = page.getByRole('dialog')
  await modal.getByLabel('Host', { exact: true }).fill(host)
  await modal.getByLabel('Porta', { exact: true }).fill('8080')
  await modal.getByLabel('Região', { exact: true }).fill('BR / São Paulo')
  await modal.getByLabel('Status operacional').selectOption('Active')
  await modal.getByLabel('Vencimento', { exact: true }).fill('2099-01-01T12:00')
  await modal.getByLabel('Usuário', { exact: true }).fill(username)
  await modal.getByLabel('Senha do proxy').fill(secret)
  await modal.getByRole('button', { name: 'Cadastrar proxy', exact: true }).click()
  await page.getByText('Proxy cadastrado com sucesso.', { exact: true }).waitFor()
}
async function edit(page, host) {
  await page.getByRole('button', { name: `Editar ${host}`, exact: true }).click()
  const modal = page.getByRole('dialog'); await modal.getByLabel('Novo usuário (opcional)').waitFor()
  assert.equal(await modal.getByLabel('Novo usuário (opcional)').inputValue(), '')
  assert.equal(await modal.getByLabel('Nova senha (opcional)').inputValue(), '')
  await screenshot(page, 'edicao-mascarada')
  await modal.getByLabel('Status operacional').selectOption('Suspended')
  await modal.getByRole('button', { name: 'Salvar alterações', exact: true }).click()
  await page.getByText('Alterações salvas com sucesso.', { exact: true }).waitFor()
}
async function safeDom(page) {
  assert.equal(await page.locator('body').innerText().then(text => text.includes(secret) || text.includes(username)), false)
  assert.equal(await page.evaluate(() => localStorage.length + sessionStorage.length), 0)
}
async function mockSuite() {
  const { context, page, state } = await mockedContext()
  stage = 'mock listagem'; await page.goto(origin + '/proxies'); await page.locator('tbody tr').first().waitFor(); await label(page)
  assert.equal(await page.locator('tbody tr').count(), 20); await page.getByText('32', { exact: true }).waitFor()
  await screenshot(page, 'desktop'); check('Mock: totais globais, tabela e navegação')
  await page.getByRole('button', { name: 'Próxima página' }).click(); await page.getByText('Página 2 de 2').waitFor()
  await page.waitForFunction(() => document.querySelectorAll('tbody tr').length === 12)
  check('Mock: paginação no servidor')
  await page.getByLabel('Ordenar por').selectOption('port')
  await page.getByRole('button', { name: 'Inverter direção da ordenação' }).click()
  await page.waitForFunction(() => document.querySelector('tbody tr')?.textContent.includes('proxy-01.example.test'))
  await page.getByLabel('Somente disponíveis').check(); await page.getByRole('button', { name: 'Aplicar filtros' }).click()
  await page.getByText('21 resultado(s)', { exact: true }).waitFor()
  assert.equal(await page.locator('tbody').getByText('Indisponível', { exact: true }).count(), 0)
  await page.getByRole('button', { name: 'Limpar', exact: true }).click(); await page.getByText('32 resultado(s)', { exact: true }).waitFor()
  check('Mock: filtros, disponíveis e ordenação no servidor')
  await page.getByLabel('Host exato').fill('inexistente.example.test'); await page.getByRole('button', { name: 'Aplicar filtros' }).click()
  await page.getByRole('heading', { name: 'Nenhum proxy encontrado' }).waitFor(); await screenshot(page, 'vazio')
  await page.getByRole('button', { name: 'Limpar', exact: true }).click(); await page.locator('tbody tr').first().waitFor()
  await page.getByRole('button', { name: 'Novo proxy', exact: true }).click(); await page.getByRole('dialog').waitFor()
  await screenshot(page, 'cadastro'); await page.keyboard.press('Escape'); await page.getByRole('dialog').waitFor({ state: 'hidden' })
  assert.ok(await page.getByRole('button', { name: 'Novo proxy', exact: true }).evaluate(element => element === document.activeElement))
  check('Mock: modal, Escape e restauração do foco')
  stage = 'Mock: cadastro e edição com filtro exato'
  await create(page, 'novo.example.test')
  await page.getByLabel('Host exato').fill('novo.example.test'); await page.getByRole('button', { name: 'Aplicar filtros' }).click()
  await page.getByRole('button', { name: 'Editar novo.example.test', exact: true }).waitFor()
  await edit(page, 'novo.example.test'); assert.deepEqual(state.lastPatch, { status: 'Suspended' })
  assert.equal(state.writes, 2); await safeDom(page); check('Mock: cadastro/PATCH/CSRF e credenciais omitidas')
  state.fail = true; await page.getByRole('button', { name: 'Atualizar', exact: true }).click(); await page.getByRole('alert').waitFor(); await screenshot(page, 'erro')
  state.fail = false; await page.getByRole('button', { name: 'Tentar novamente', exact: true }).click(); await page.locator('tbody tr').first().waitFor()
  check('Mock: estado de erro e recuperação')
  await context.close()
  const mobile = await mockedContext('Operator', { width: 390, height: 844 })
  await mobile.page.goto(origin + '/proxies'); await mobile.page.locator('tbody tr').first().waitFor(); await label(mobile.page)
  assert.equal(await mobile.page.getByRole('button', { name: 'Novo proxy', exact: true }).count(), 0)
  assert.equal(await mobile.page.getByRole('button', { name: /^Editar / }).count(), 0)
  assert.ok(await mobile.page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth))
  await screenshot(mobile.page, 'mobile-operator'); check('Mock: mobile 390px sem overflow e Operator somente leitura')
  await mobile.context.close()
  const mobileForm = await mockedContext('Manager', { width: 390, height: 844 })
  await mobileForm.page.goto(origin + '/proxies'); await mobileForm.page.getByRole('button', { name: 'Novo proxy', exact: true }).click(); await mobileForm.page.getByRole('dialog').waitFor(); await label(mobileForm.page)
  for (let i = 0; i < 14; i++) {
    await mobileForm.page.keyboard.press('Tab')
    assert.ok(await mobileForm.page.getByRole('dialog').evaluate(dialog => dialog.contains(document.activeElement)))
  }
  await mobileForm.page.getByRole('dialog').evaluate(dialog => { dialog.scrollTop = 0 })
  await screenshot(mobileForm.page, 'mobile-cadastro'); check('Mock: formulário mobile e foco contido por Tab')
  await mobileForm.page.getByRole('dialog').evaluate(dialog => { dialog.scrollTop = dialog.scrollHeight })
  await screenshot(mobileForm.page, 'mobile-cadastro-credenciais')
  await mobileForm.context.close()
}
async function realSuite() {
  assert.equal(apiOrigin, 'https://localhost:7091'); assert.match(process.env.CENTRAL_UI_DATABASE || '', /^p6_ui_test_[a-f0-9]{32}$/)
  assert.ok(process.env.CENTRAL_UI_PASSWORD)
  const api = await playwrightRequest.newContext({ ignoreHTTPSErrors: true })
  let healthy = false
  for (let attempt = 0; attempt < 60 && !healthy; attempt++) {
    try { healthy = (await api.get(apiOrigin + '/health', { timeout: 2000 })).ok() } catch { /* inicialização local */ }
    if (!healthy) await new Promise(resolve => setTimeout(resolve, 500))
  }
  assert.ok(healthy)
  await api.dispose()
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 1050 }, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo' })
  let patch = null
  const responseChecks = []
  const page = await context.newPage(); page.on('pageerror', () => { pageErrors++ })
  page.on('request', request => { if (request.method() === 'PATCH' && new URL(request.url()).pathname.startsWith('/api/')) patch = request.postDataJSON() })
  page.on('response', response => {
    const incoming = new URL(response.url()); if (!incoming.pathname.startsWith('/api/')) return
    responseStatuses.push({ method: response.request().method(), path: incoming.pathname.replace(/[a-f0-9-]{36}/g, '{id}'), status: response.status() })
    if (incoming.pathname.includes('/proxies')) responseChecks.push((async () => {
      // Queries canceladas ao trocar sessão podem não concluir response.text().
      // Limite explícito evita prender o harness esperando um corpo abandonado.
      let timeout
      const text = await Promise.race([
        response.text().catch(() => undefined),
        new Promise(resolve => { timeout = setTimeout(() => resolve(undefined), 2000) }),
      ])
      clearTimeout(timeout)
      if (text === undefined) unreadResponses++
      else if (text.includes(secret) || text.includes(username) || text.includes('ciphertext') || text.includes('keyId')) sensitiveResponses++
    })())
  })
  async function login(email) {
    await page.getByLabel('E-mail').fill(email); await page.getByLabel('Senha', { exact: true }).fill(process.env.CENTRAL_UI_PASSWORD)
    await page.getByRole('button', { name: 'Entrar', exact: true }).click(); await page.getByRole('heading', { name: 'Início', exact: true }).waitFor()
    await page.getByRole('link', { name: 'HTTP - FACEBOOK', exact: true }).click(); await page.getByRole('heading', { name: 'Gestão de proxies' }).waitFor()
  }
  stage = 'API real login'; await page.goto(origin + '/proxies'); await page.getByRole('heading', { name: 'Acesso interno' }).waitFor(); await label(page)
  await login('admin-ui@example.test'); await page.getByRole('heading', { name: 'Nenhum proxy encontrado' }).waitFor(); check('API real: login e listagem vazia')
  const cookie = (await context.cookies()).find(cookie => cookie.name === '__Host-Central.Session')
  assert.ok(cookie && cookie.httpOnly && cookie.secure && cookie.sameSite === 'Strict'); check('API real: cookie Secure/HttpOnly/SameSite=Strict')
  const csrfStatus = await page.evaluate(async () => (await fetch('/api/v1/proxies', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' })).status)
  assert.equal(csrfStatus, 400); check('API real: escrita sem CSRF recusada')
  await create(page, 'browser-real.example.test'); await edit(page, 'browser-real.example.test'); assert.deepEqual(patch, { status: 'Suspended' })
  await safeDom(page); await screenshot(page, 'desktop'); check('API real: cadastro 201, PATCH, resposta mascarada e invalidação')
  for (const role of ['Operator', 'Manager']) {
    const status = await page.evaluate(async ({ role, password }) => {
      const { token } = await (await fetch('/api/v1/auth/csrf')).json()
      return (await fetch('/api/v1/users', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token }, body: JSON.stringify({ email: `${role.toLowerCase()}-ui@example.test`, password, role }) })).status
    }, { role, password: process.env.CENTRAL_UI_PASSWORD })
    assert.equal(status, 201)
  }
  await page.getByRole('button', { name: 'Sair', exact: true }).click(); await page.getByRole('heading', { name: 'Acesso interno' }).waitFor()
  assert.equal(await page.evaluate(async () => (await fetch('/api/v1/auth/me')).status), 401); check('API real: logout encerra sessão')
  await login('operator-ui@example.test'); await page.locator('tbody tr').first().waitFor()
  assert.equal(await page.getByRole('button', { name: 'Novo proxy', exact: true }).count(), 0)
  const denied = await page.evaluate(async () => {
    const { token } = await (await fetch('/api/v1/auth/csrf')).json()
    return (await fetch('/api/v1/proxies', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token }, body: '{}' })).status
  }); assert.equal(denied, 403)
  await screenshot(page, 'operator'); check('API real: Operator lê; escrita forçada recebe 403')
  await page.getByRole('button', { name: 'Sair', exact: true }).click(); await page.getByRole('heading', { name: 'Acesso interno' }).waitFor()
  await login('manager-ui@example.test'); await page.getByRole('button', { name: 'Novo proxy', exact: true }).waitFor()
  check('API real: Manager vê ações de gestão')
  await page.waitForFunction(() => [...document.querySelectorAll('button')].some(button => button.textContent === 'Atualizar' && !button.disabled))
  await context.clearCookies()
  assert.equal((await context.cookies()).length, 0)
  assert.equal(await page.evaluate(async () => (await fetch('/api/v1/auth/me', { cache: 'no-store', signal: AbortSignal.timeout(5000) })).status), 401)
  await page.getByRole('button', { name: 'Atualizar', exact: true }).click()
  await page.getByText('Sessão expirada. Entre novamente.', { exact: true }).waitFor(); check('API real: perda de sessão retorna ao login')
  await Promise.all(responseChecks)
  assert.equal(sensitiveResponses, 0)
  await context.close()
}
;(async () => {
  fs.mkdirSync(artifacts, { recursive: true })
  if (real) {
    assert.equal(apiOrigin, 'https://localhost:7091')
    const { createServer } = await import(pathToFileURL(require.resolve('../src/central-contingencia-web/node_modules/vite')).href)
    const webRoot = path.join(root, 'src/central-contingencia-web')
    isolatedVite = await createServer({ configFile: path.join(webRoot, 'vite.config.ts'), root: webRoot,
      server: { host: 'localhost', port: 5174, strictPort: true, proxy: { '/api': { target: apiOrigin, secure: false } } } })
    await isolatedVite.listen()
  }
  browser = await chromium.launch({ executablePath: process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true })
  await (real ? realSuite() : mockSuite())
  assert.equal(pageErrors, 0); check('Chrome: nenhuma exceção de JavaScript')
  fs.writeFileSync(path.join(artifacts, `${mode}-results.json`), JSON.stringify({ mode, passed: true, chromeVersion: browser.version(), executedAt: new Date().toISOString(), checks, pageErrors, unreadResponses, sensitiveResponses, responseStatuses, screenshots: fs.readdirSync(artifacts).filter(file => file.startsWith(mode) && file.endsWith('.png') && !file.includes('falha')) }, null, 2))
  console.log(`${mode}: ${checks.length} verificações aprovadas; capturas e resultado sanitizado em .local/verification-2.2b.`)
})().catch(async error => {
  const failedPage = browser?.contexts().flatMap(context => context.pages()).at(-1)
  if (failedPage) await screenshot(failedPage, 'falha').catch(() => {})
  const errorType = error instanceof Error ? error.name : 'UnknownError'
  const location = error instanceof Error ? error.stack?.split('\n').find(line => line.trim().startsWith('at ') && line.includes('Verify-ProxyUi.cjs:')) : undefined
  fs.mkdirSync(artifacts, { recursive: true }); fs.writeFileSync(path.join(artifacts, `${mode}-results.json`), JSON.stringify({ mode, passed: false, completedChecks: checks, failedStage: stage, pageErrors, errorType, location, responseStatuses }, null, 2))
  console.error(`Verificação falhou no cenário: ${stage}. Detalhes sensíveis foram omitidos.`); process.exitCode = 1
}).finally(async () => { if (browser) await browser.close(); if (isolatedVite) await isolatedVite.close() })
