// Chrome isolado; todas as chamadas /api são mocks sintéticos. Sem HAR ou traces.
const { chromium } = require('../src/central-contingencia-web/node_modules/playwright-core')
const assert = require('node:assert/strict')
const fs = require('node:fs')
const path = require('node:path')
const artifacts = path.resolve(__dirname, '../.local/verification-2.3c.1')
const origin = 'https://localhost:5173'
const secret = 'synthetic-secret-not-for-display'
let browser
let stage = 'inicialização'
let consoleLeaks = 0
let pageErrors = 0
const checks = []
function preview(state, page) {
  const invalid = state.invalid
  return { columns: [{ position: 8, field: 'DATA DE VENCIMENTO' }], headerIssues: [], page, pageSize: 100,
    summary: { total: 3, valid: invalid || state.skipping ? 2 : 3, invalid: invalid || state.skipping ? 1 : 0, duplicates: 0, empty: state.skipping ? 1 : 0, conflicts: state.skipping ? 0 : 1, warnings: 1 },
    items: [2, 3, 4].map(line => ({ line, endLine: line, hostMasked: '***', port: state.skipping && line === 4 ? null : 8080, status: state.skipping && line === 4 ? null : 'Active', expiresAt: invalid && line === 2 || state.skipping && line === 4 ? null : line === 2 ? `${state.referenceYear || '2099'}-12-31T00:00:00Z` : '2098-12-31T00:00:00Z', regionPresent: false,
      valid: !invalid || line !== 2, duplicate: false, existingConflict: line === 4,
      errors: invalid && line === 2 ? [{ line: 2, column: 8, code: 'ExpiryYearRequired', message: 'synthetic unused message' }] : state.skipping && line === 4 ? [{ line, column: null, code: 'EmptyRow', message: '' }] : [],
      warnings: line === 4 ? [{ line, column: null, code: state.skipping ? 'NoConnectionRowSkipped' : 'ExistingEndpoint', message: '' }] : [] })),
    confirmationToken: 'browser-synthetic-protected-proof', confirmationExpiresAt: new Date(Date.now() + (state.expired ? -1000 : 900000)).toISOString() }
}
async function context(role = 'Admin', viewport = { width: 1440, height: 1050 }) {
  const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport, locale: 'pt-BR' })
  const state = { invalid: false, expired: false, previewStatus: 200, commitStatus: 200, keys: [], writes: 0, region: '' }
  await ctx.addInitScript(() => {
    document.addEventListener('DOMContentLoaded', () => {
      const banner = document.createElement('div'); banner.textContent = 'TESTE COM MOCKS — dados sintéticos; nenhuma gravação homologada'
      banner.style.cssText = 'background:#fef3c7;padding:8px;text-align:center;font:12px system-ui'; document.body.prepend(banner)
    })
  })
  await ctx.route(url => url.pathname.startsWith('/api/'), async route => {
    const request = route.request(), url = new URL(request.url())
    const reply = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
    if (url.pathname.endsWith('/me')) return reply({ id: 'synthetic-admin', email: 'synthetic-admin@example.test', roles: [role] })
    if (url.pathname.endsWith('/csrf')) return reply({ token: 'synthetic-csrf' })
    if (url.pathname.endsWith('/preview') || url.pathname.endsWith('/commit')) {
      const headers = request.headers()
      assert.equal(headers['x-csrf-token'], 'synthetic-csrf')
      assert.ok(headers['content-type'].startsWith('multipart/form-data; boundary='))
      assert.ok(request.postDataBuffer().includes(Buffer.from(secret)))
      if (url.pathname.endsWith('/preview')) {
        state.skipping = url.searchParams.get('skipRowsWithoutConnection') === 'true'
        state.referenceYear = url.searchParams.get('referenceYear')
        return state.previewStatus === 200 ? reply(preview(state, Number(url.searchParams.get('page')))) : reply({}, state.previewStatus)
      }
      assert.equal(headers['x-import-confirmation'], 'browser-synthetic-protected-proof')
      assert.match(headers['idempotency-key'], /^[0-9a-f-]{36}$/)
      state.keys.push(headers['idempotency-key']); state.writes++; state.region = url.searchParams.get('defaultRegion')
      assert.equal(url.searchParams.get('skipRowsWithoutConnection') === 'true', Boolean(state.skipping))
      assert.equal(url.searchParams.get('referenceYear'), state.referenceYear)
      await new Promise(resolve => setTimeout(resolve, 150))
      if (state.commitStatus !== 200) return reply({}, state.commitStatus)
      return reply({ importId: '00000000-0000-4000-8000-000000000099', replayed: false, total: 3, imported: 2, ignored: state.skipping ? 1 : 0, invalid: 0, conflicting: state.skipping ? 0 : 1,
        items: [{ line: 2, proxyId: '00000000-0000-4000-8000-000000000001', outcome: 'Imported' }, { line: 3, proxyId: '00000000-0000-4000-8000-000000000002', outcome: 'Imported' }, { line: 4, proxyId: null, outcome: state.skipping ? 'Empty' : 'Conflict' }] })
    }
    if (url.pathname === '/api/v1/proxies' || url.pathname.endsWith('/available')) return reply({ page: 1, pageSize: 20, total: 0, items: [] })
    return route.abort() // Nunca deixar um endpoint não previsto chegar à API real.
  })
  const page = await ctx.newPage()
  page.on('pageerror', () => pageErrors++)
  page.on('console', message => { if ([secret, 'browser-synthetic-protected-proof'].some(value => message.text().includes(value))) consoleLeaks++ })
  return { ctx, page, state }
}
async function select(page) { await page.getByLabel('Arquivo CSV', { exact: true }).setInputFiles({ name: 'synthetic.csv', mimeType: 'text/csv', buffer: Buffer.from(`PERFIL,PLATAFORMA,PROXY,PORTA,USUARIO,SENHA,STATUS,DATA DE VENCIMENTO\np,FACEBOOK,synthetic.test,8080,synthetic-user,${secret},ATIVO,31/12\np2,FACEBOOK,other.synthetic.test,8080,synthetic-user,${secret},ATIVO,31/12/2098\np3,FACEBOOK,,,,,ATIVO,31/12`) }) }
async function analyze(page) { await page.getByRole('button', { name: 'Gerar prévia', exact: true }).click(); await page.getByText('Resumo do arquivo completo', { exact: true }).waitFor() }
async function safety(page) {
  assert.ok(!(await page.locator('body').innerText()).includes(secret))
  assert.ok(!(await page.locator('body').innerText()).includes('browser-synthetic-protected-proof'))
  assert.equal(await page.evaluate(() => localStorage.length + sessionStorage.length), 0)
}
;(async () => {
  fs.mkdirSync(artifacts, { recursive: true })
  browser = await chromium.launch({ executablePath: process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true })
  stage = 'prévia desktop'
  const { ctx, page, state } = await context()
  await page.goto(origin + '/proxies'); await page.getByRole('link', { name: 'Importar CSV' }).click()
  await select(page); await page.getByLabel('Região complementar (opcional)').fill('BR / São Paulo')
  const year = page.getByLabel('Ano de referência (opcional)'); assert.equal(await year.inputValue(), ''); await year.fill('2099')
  const skip = page.getByLabel(/Ignorar explicitamente linhas com host/); assert.equal(await skip.isChecked(), false); await skip.check(); await analyze(page)
  await page.getByText(/31\/12\/2099.*UTC/).waitFor(); await page.getByText(/31\/12\/2098.*UTC/).first().waitFor()
  await safety(page); await page.screenshot({ path: path.join(artifacts, '01-previa-desktop.png'), fullPage: true })
  checks.push('Seleção/multipart/CSRF/prévia/credenciais mascaradas/região e ano explícitos/datas completas preservadas')
  stage = 'cancelamento e confirmação'
  await page.getByRole('button', { name: 'Revisar confirmação' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Cancelar', exact: true }).click(); assert.equal(state.writes, 0)
  await page.getByRole('button', { name: 'Revisar confirmação' }).click()
  await page.screenshot({ path: path.join(artifacts, '02-confirmacao.png'), fullPage: true })
  const button = page.getByRole('dialog').getByRole('button', { name: 'Confirmar importação', exact: true })
  await button.click(); await page.getByRole('heading', { name: 'Relatório da importação' }).waitFor(); assert.equal(state.writes, 1); assert.equal(state.region, 'BR / São Paulo')
  await safety(page); await page.screenshot({ path: path.join(artifacts, '03-relatorio.png'), fullPage: true })
  checks.push('Confirmação explícita/cancelamento/idempotency header/relatório de conflito')
  await ctx.close()
  stage = 'mobile e erros'
  const mobile = await context('Admin', { width: 390, height: 844 })
  await mobile.page.goto(origin + '/proxies'); await mobile.page.getByRole('link', { name: 'Importar CSV' }).waitFor()
  assert.equal(await mobile.page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true)
  await mobile.page.getByRole('link', { name: 'Importar CSV' }).click(); await select(mobile.page)
  mobile.state.invalid = true; await analyze(mobile.page)
  assert.equal(await mobile.page.getByRole('button', { name: 'Revisar confirmação' }).isDisabled(), true)
  await mobile.page.screenshot({ path: path.join(artifacts, '04-invalido-mobile.png'), fullPage: true })
  mobile.state.invalid = false; mobile.state.expired = true; await analyze(mobile.page)
  assert.equal(await mobile.page.getByRole('button', { name: 'Revisar confirmação' }).isDisabled(), true)
  mobile.state.expired = false; await analyze(mobile.page); mobile.state.commitStatus = 503
  await mobile.page.getByRole('button', { name: 'Revisar confirmação' }).click(); await mobile.page.getByRole('dialog').getByRole('button', { name: 'Confirmar importação', exact: true }).click()
  await mobile.page.getByRole('alert').waitFor(); await mobile.page.getByRole('dialog').waitFor({ state: 'hidden' })
  mobile.state.commitStatus = 200; await mobile.page.getByRole('button', { name: 'Revisar confirmação' }).click(); await mobile.page.getByRole('dialog').getByRole('button', { name: 'Confirmar importação', exact: true }).click()
  await mobile.page.getByRole('heading', { name: 'Relatório da importação' }).waitFor(); assert.equal(mobile.state.keys[0], mobile.state.keys[1]); await safety(mobile.page)
  checks.push('Mobile/validação inválida/prova expirada/503 e retry com mesma chave')
  await mobile.ctx.close()
  stage = 'permissões'
  for (const role of ['Manager', 'Operator']) {
    const restricted = await context(role); await restricted.page.goto(origin + '/proxies')
    await restricted.page.getByRole('heading', { name: 'Gestão de proxies' }).waitFor(); assert.equal(await restricted.page.getByRole('link', { name: 'Importar CSV' }).count(), 0)
    await restricted.page.goto(origin + '/proxies/import'); await restricted.page.getByRole('heading', { name: 'Acesso negado' }).waitFor(); await restricted.ctx.close()
  }
  assert.equal(consoleLeaks, 0); assert.equal(pageErrors, 0)
  checks.push('Manager/Operator sem acesso; nenhuma credencial no DOM/storage/console')
  fs.writeFileSync(path.join(artifacts, 'results.json'), JSON.stringify({ passed: true, mode: 'Chrome com mocks sintéticos', checks, consoleLeaks, pageErrors }, null, 2))
  console.log('Chrome/Playwright aprovado: '+checks.length+' grupos de verificações e quatro capturas; API real não utilizada.')
})().catch(() => { console.error('Validação Chrome falhou na etapa: '+stage+'; detalhes sensíveis omitidos.'); process.exitCode = 1 }).finally(async () => { await browser?.close() })
