// Chrome real; every API request is intercepted. No homologated writes or real data.
const { chromium } = require('../src/central-contingencia-web/node_modules/playwright-core')
const assert = require('node:assert/strict')
const fs = require('node:fs')
const path = require('node:path')
const { randomUUID } = require('node:crypto')
const origin = 'https://localhost:5173'
const artifacts = path.resolve(__dirname, '../.local/verification-2.4c2')
const checks = [], errors = [], leaks = [], networkCalls = []
let browser, stage = 'startup'
const mark = label => { stage = label; checks.push(label) }
const hidden = ['SyntheticProxy!123', 'synthetic-sensitive-username', 'synthetic-grid-csrf']
async function context(role = 'Admin') {
  const ctx = await browser.newContext({ ignoreHTTPSErrors: true, serviceWorkers: 'block', viewport: { width: 1440, height: 1000 }, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo' })
  const profiles = Array.from({ length: 40 }, (_, i) => ({ id: randomUUID(), name: `Perfil ${String(i + 1).padStart(3, '0')}`, platform: '', platform1: 'DARKPOST', platform2: 'TIKTOK', status: 'Active', createdAt: '2026-10-12T00:00:00Z', updatedAt: '2026-10-12T00:00:00Z', closedAt: null }))
  const states = ['Available', 'Reserved', 'InUse', 'Unavailable']
  const rows = profiles.map((profile, i) => ({ proxyId: randomUUID(), profileId: [1, 2].includes(i % 4) ? profile.id : null, profileName: [1, 2].includes(i % 4) ? profile.name : null,
    platform1: [1, 2].includes(i % 4) ? 'DARKPOST' : null, platform2: [1, 2].includes(i % 4) ? 'TIKTOK' : null,
    host: `proxy-${String(i + 1).padStart(3, '0')}.synthetic.test`, port: 8080, region: i % 2 ? 'Brasil Sul' : 'Brasil Norte',
    expiresAt: [2, 3].includes(i % 4) ? '2000-01-01T12:00:00Z' : `2099-01-${String(i + 1).padStart(2, '0')}T12:00:00Z`,
    status: i % 4 === 2 ? 'Suspended' : 'Active', availability: states[i % 4], allocationId: [1, 2].includes(i % 4) ? randomUUID() : null,
    reservationExpiresAt: i % 4 === 1 ? new Date(Date.now() + 900000).toISOString() : null }))
  // Valid dates for all forty synthetic records.
  rows.forEach((row, i) => { if (![2, 3].includes(i % 4)) row.expiresAt = new Date(Date.UTC(2099, 0, i + 1, 12)).toISOString() })
  const allocations = rows.filter(row => row.allocationId).map(row => ({ id: row.allocationId, proxyId: row.proxyId, profileId: row.profileId, status: row.availability === 'InUse' ? 'Confirmed' : 'Reserved', createdAt: '2026-10-12T00:00:00Z', updatedAt: '2026-10-12T00:00:00Z', reservationExpiresAt: row.reservationExpiresAt ?? '2026-10-12T00:15:00Z', confirmedAt: row.availability === 'InUse' ? '2026-10-12T00:01:00Z' : null, endedAt: null, endReason: null }))
  const state = { profiles, rows, allocations, readStatus: 200, writes: [], writeStatus: 200 }
  await ctx.addInitScript(() => document.addEventListener('DOMContentLoaded', () => { const banner = document.createElement('div'); banner.textContent = 'TESTE COM MOCKS — dados sintéticos; nenhuma gravação homologada'; banner.style.cssText = 'padding:6px;background:#fef3c7;text-align:center;font:12px system-ui'; document.body.prepend(banner) }))
  await ctx.route(url => url.pathname.startsWith('/api/'), async route => {
    const req = route.request(), url = new URL(req.url()), endpoint = url.pathname, method = req.method()
    networkCalls.push({ method, endpoint: endpoint.replace(/[0-9a-f-]{36}/g, '{id}'), role })
    const reply = (data, status = 200) => route.fulfill({ status, contentType: 'application/json', body: status === 204 ? '' : JSON.stringify(data) })
    if (endpoint.endsWith('/me')) return reply({ id: 'synthetic-' + role, email: role.toLowerCase() + '@example.test', roles: [role] })
    if (endpoint.endsWith('/csrf')) return reply({ token: 'synthetic-grid-csrf' })
    if (method !== 'GET') {
      state.writes.push(endpoint); assert.equal(req.headers()['x-csrf-token'], 'synthetic-grid-csrf')
      if (role === 'Operator') return reply({}, 403)
      if (state.writeStatus !== 200) return reply({}, state.writeStatus)
      const body = req.postDataJSON()
      if (endpoint === '/api/v1/profiles') { const profile = { ...profiles[0], ...body, id: randomUUID() }; profiles.push(profile); return reply(profile, 201) }
      if (endpoint.endsWith('/reserve')) {
        assert.match(req.headers()['idempotency-key'], /^[0-9a-f-]{36}$/)
        const profile = profiles.find(p => p.id === body.profileId), row = rows.find(r => r.proxyId === body.proxyId)
        assert.ok(profile && row)
        if (row.availability !== 'Available') return reply({}, 409)
        const allocation = { id: randomUUID(), ...body, status: 'Reserved', createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(), reservationExpiresAt: new Date(Date.now() + 900000).toISOString(), confirmedAt: null, endedAt: null, endReason: null }
        allocations.push(allocation); Object.assign(row, { availability: 'Reserved', profileId: profile.id, profileName: profile.name, platform1: profile.platform1, platform2: profile.platform2, allocationId: allocation.id, reservationExpiresAt: allocation.reservationExpiresAt }); return reply(allocation, 201)
      }
      if (endpoint.endsWith('/confirm') || endpoint.endsWith('/release')) {
        const allocation = allocations.find(a => endpoint.includes(a.id)), row = rows.find(r => r.proxyId === allocation?.proxyId); assert.ok(allocation && row)
        if (endpoint.endsWith('/confirm')) { allocation.status = 'Confirmed'; allocation.confirmedAt = new Date().toISOString(); row.availability = 'InUse'; row.reservationExpiresAt = null }
        else { allocation.status = 'Released'; allocation.endedAt = new Date().toISOString(); allocation.endReason = 'Released'; Object.assign(row, { availability: 'Available', profileId: null, profileName: null, platform1: null, platform2: null, allocationId: null, reservationExpiresAt: null }) }
        return reply({}, 204)
      }
      if (endpoint === '/api/v1/proxies') return reply({ id: randomUUID(), host: body.host, port: body.port, region: body.region, status: body.status, usernameMasked: '***', expiresAt: body.expiresAt, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(), available: false }, 201)
      return reply({}, 400)
    }
    if (state.readStatus !== 200) return reply({}, state.readStatus)
    const page = Number(url.searchParams.get('page') || 1), size = Number(url.searchParams.get('pageSize') || 20)
    const paged = items => reply({ items: items.slice((page - 1) * size, page * size), total: items.length, page, pageSize: size, evaluatedAt: new Date().toISOString() })
    if (endpoint.endsWith('/operational-grid')) {
      let items = rows.filter(row => (!url.searchParams.has('profile') || row.profileName?.toLowerCase().includes(url.searchParams.get('profile').toLowerCase())) && (!url.searchParams.has('region') || row.region.toLowerCase().includes(url.searchParams.get('region').toLowerCase())) && (!url.searchParams.has('platform') || [row.platform1, row.platform2].includes(url.searchParams.get('platform'))) && (!url.searchParams.has('availability') || row.availability === url.searchParams.get('availability')))
      items = [...items].sort((a, b) => (Date.parse(a.expiresAt) - Date.parse(b.expiresAt)) * (url.searchParams.get('direction') === 'desc' ? -1 : 1) || a.proxyId.localeCompare(b.proxyId)); return paged(items)
    }
    if (endpoint === '/api/v1/profiles') return paged(profiles.filter(p => !url.searchParams.has('name') || p.name === url.searchParams.get('name')))
    if (endpoint === '/api/v1/allocations') return paged(allocations.filter(a => a.profileId === url.searchParams.get('profileId') && a.status === url.searchParams.get('status')))
    if (/\/profiles\/[^/]+\/allocations$/.test(endpoint)) return paged(allocations.filter(a => a.profileId === endpoint.split('/')[4] && (!url.searchParams.has('status') || a.status === url.searchParams.get('status'))))
    const profile = profiles.find(p => endpoint === '/api/v1/profiles/' + p.id); if (profile) return reply(profile)
    const row = rows.find(r => endpoint === '/api/v1/proxies/' + r.proxyId); if (row) return reply({ id: row.proxyId, host: row.host, port: row.port, region: row.region, status: row.status, usernameMasked: '***', expiresAt: row.expiresAt, createdAt: '2026-10-12T00:00:00Z', updatedAt: '2026-10-12T00:00:00Z', available: row.availability === 'Available' })
    return reply({}, 404)
  })
  const page = await ctx.newPage(); page.on('pageerror', error => errors.push(error.name)); page.on('console', message => { if (hidden.some(s => message.text().includes(s))) leaks.push('console') })
  return { ctx, page, state }
}
async function safe(page) {
  assert.equal(await page.evaluate(() => localStorage.length + sessionStorage.length), 0)
  const text = await page.locator('body').innerText(); for (const secret of hidden) assert.ok(!text.includes(secret))
}
(async () => {
  fs.mkdirSync(artifacts, { recursive: true }); browser = await chromium.launch({ channel: 'chrome', headless: true })
  const { ctx, page, state } = await context()
  mark('Grid compacto, nove colunas, estados e perfil vencido preservado')
  await page.goto(origin + '/proxies'); await page.getByRole('region', { name: 'Grid operacional' }).waitFor()
  assert.deepEqual(await page.locator('thead th').allTextContents(), ['Perfil', 'Plataforma 1', 'Plataforma 2', 'Proxy', 'Porta', 'Região', 'Vencimento', 'Disponibilidade', 'Ações'])
  assert.equal(await page.locator('tbody tr').count(), 20); assert.equal(await page.getByRole('navigation').getByRole('link', { name: 'Perfis', exact: true }).count(), 0)
  await safe(page); await page.screenshot({ path: path.join(artifacts, '01-grid-desktop.png'), fullPage: true })
  mark('Filtros combinados, debounce e restauração por URL')
  await page.getByLabel('Perfil', { exact: true }).fill('Perfil'); await page.getByLabel('Região', { exact: true }).fill('sul'); await page.getByLabel('Plataforma', { exact: true }).selectOption('TIKTOK'); await page.getByLabel('Disponibilidade', { exact: true }).selectOption('Reserved')
  await page.waitForURL(url => url.searchParams.get('profile') === 'Perfil' && url.searchParams.get('region') === 'sul'); await page.getByText(/10 resultado/).waitFor()
  await page.reload(); await page.getByRole('region', { name: 'Grid operacional' }).waitFor(); assert.equal(await page.getByLabel('Região', { exact: true }).inputValue(), 'sul')
  mark('Detalhes e volta ao grid preservam filtros')
  await page.locator('tbody a').filter({ hasText: 'Perfil' }).first().click(); await page.getByRole('link', { name: 'Voltar ao grid' }).click(); await page.getByRole('heading', { name: 'HTTP - FACEBOOK' }).waitFor(); assert.equal(new URL(page.url()).searchParams.get('platform'), 'TIKTOK')
  await page.screenshot({ path: path.join(artifacts, '02-grid-filtrado.png'), fullPage: true })
  mark('Ordenação, paginação e tamanho enviados ao servidor')
  await page.getByRole('button', { name: 'Limpar', exact: true }).click(); await page.getByRole('button', { name: 'Próxima página de proxies' }).click(); await page.waitForURL(url => url.searchParams.get('page') === '2')
  await page.getByLabel('Vencimento', { exact: true }).selectOption('desc'); await page.waitForURL(url => url.searchParams.get('page') === '1' && url.searchParams.get('direction') === 'desc'); await page.getByLabel('Por página', { exact: true }).selectOption('100'); await page.waitForFunction(() => document.querySelectorAll('tbody tr').length === 40)
  mark('Reserva da linha, confirmação, cancelamento/liberação e histórico')
  await page.getByRole('button', { name: 'Reservar ' + state.rows[0].host, exact: true }).click(); await page.getByRole('radio', { name: state.profiles[0].name, exact: true }).check(); await page.getByRole('button', { name: 'Reservar para o perfil selecionado' }).click(); await page.getByRole('button', { name: 'Confirmar reserva', exact: true }).waitFor(); await page.screenshot({ path: path.join(artifacts, '03-reserva-detalhes.png'), fullPage: true })
  await page.getByRole('button', { name: 'Confirmar reserva', exact: true }).click(); await page.getByText('Reserva confirmada.', { exact: true }).waitFor(); await page.getByRole('button', { name: 'Liberar proxy', exact: true }).click(); await page.getByRole('dialog').getByRole('button', { name: 'Cancelar', exact: true }).click(); assert.equal(state.writes.filter(p => p.endsWith('/release')).length, 0)
  await page.getByRole('button', { name: 'Liberar proxy', exact: true }).click(); await page.getByRole('dialog').getByRole('button', { name: 'Confirmar liberação' }).click(); await page.getByText(/Alocação liberada/).waitFor(); await page.getByText('Liberação', { exact: true }).waitFor(); await page.getByRole('link', { name: 'Voltar ao grid' }).click(); await page.getByRole('button', { name: 'Reservar ' + state.rows[0].host, exact: true }).waitFor()
  mark('Conflito 409 não repete operação')
  state.writeStatus = 409; await page.getByRole('button', { name: 'Reservar ' + state.rows[0].host, exact: true }).click(); await page.getByRole('radio', { name: state.profiles[0].name, exact: true }).check(); await page.getByRole('button', { name: 'Reservar para o perfil selecionado' }).click(); await page.getByRole('alert').waitFor(); await page.screenshot({ path: path.join(artifacts, '04-conflito.png'), fullPage: true }); await page.getByRole('button', { name: 'Cancelar', exact: true }).click()
  mark('Cadastro de perfil com duas atribuições, sem plataforma legada obrigatória')
  state.writeStatus = 200; await page.getByRole('link', { name: 'Administrar perfis', exact: true }).click(); await page.getByRole('button', { name: 'Novo perfil', exact: true }).click(); await page.getByLabel('Nome do perfil').fill('Perfil criado sintético'); await page.getByLabel('Plataforma 1', { exact: true }).selectOption('FLAG'); await page.getByLabel('Plataforma 2', { exact: true }).selectOption('GOOGLE ADS'); await page.getByRole('button', { name: 'Cadastrar perfil', exact: true }).click(); await page.getByRole('heading', { name: 'Perfil criado sintético' }).waitFor(); await page.getByRole('link', { name: 'Voltar ao grid' }).click()
  mark('Mobile não causa overflow da página; tabela rola internamente')
  await page.setViewportSize({ width: 390, height: 844 }); await page.getByRole('region', { name: 'Grid operacional' }).waitFor(); assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)); await page.screenshot({ path: path.join(artifacts, '05-grid-mobile.png'), fullPage: true }); await safe(page)
  mark('Erro 503 e recuperação')
  state.readStatus = 503; await page.getByRole('button', { name: 'Atualizar', exact: true }).click(); await page.getByRole('alert').waitFor(); state.readStatus = 200; await page.getByRole('button', { name: 'Tentar novamente', exact: true }).click(); await page.getByRole('region', { name: 'Grid operacional' }).waitFor(); await ctx.close()
  for (const role of ['Manager', 'Operator']) {
    mark('Permissões ' + role); const test = await context(role); await test.page.goto(origin + '/proxies'); await test.page.getByRole('region', { name: 'Grid operacional' }).waitFor(); assert.equal(await test.page.getByRole('link', { name: 'Importar CSV', exact: true }).count(), 0)
    assert.equal(await test.page.getByRole('button', { name: 'Novo proxy', exact: true }).count(), role === 'Manager' ? 1 : 0)
    if (role === 'Operator') { assert.equal(await test.page.getByRole('button', { name: /^Editar / }).count(), 0); await test.page.screenshot({ path: path.join(artifacts, '06-operator.png'), fullPage: true }) }
    await safe(test.page); await test.ctx.close()
  }
  assert.equal(errors.length, 0); assert.equal(leaks.length, 0)
  fs.writeFileSync(path.join(artifacts, 'results.json'), JSON.stringify({ passed: true, mode: 'mocks', checks, pageErrors: errors.length, leaks: leaks.length, requests: networkCalls }, null, 2))
  console.log(`Chrome/mocks: ${checks.length} verificações aprovadas; sem escrita homologada ou exposição de credenciais.`)
})().catch(error => { console.error('Playwright falhou em: ' + stage + '; ' + error.message); process.exitCode = 1 }).finally(async () => { await browser?.close() })
