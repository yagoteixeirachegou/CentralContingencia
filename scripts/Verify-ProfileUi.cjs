// Chrome local. Mocks intercept every /api request; --real uses only an isolated API/database.
const { chromium, request } = require('../src/central-contingencia-web/node_modules/playwright-core')
const assert = require('node:assert/strict')
const fs = require('node:fs')
const path = require('node:path')
const { randomUUID } = require('node:crypto')
const { pathToFileURL } = require('node:url')
const root = path.resolve(__dirname, '..')
const artifacts = path.join(root, '.local/verification-2.4c')
const real = process.argv.includes('--real')
const mode = real ? 'api-descartavel' : 'mocks'
const origin = real ? 'https://localhost:5174' : 'https://localhost:5173'
let browser, vite, stage = 'inicialização', pageErrors = 0, leaks = 0
const checks = [], responses = [], responseTasks = []
const secret = 'SyntheticProxy!123', username = 'synthetic-sensitive-profile-user'
const check = label => { stage = label; checks.push(label) }
const screenshot = (page, label) => page.screenshot({ path: path.join(artifacts, `${mode}-${label}.png`), fullPage: true })
async function watch(context) {
  await context.addInitScript(label => {
    document.addEventListener('DOMContentLoaded', () => {
      const banner = document.createElement('div'); banner.textContent = label
      banner.style.cssText = 'background:#fef3c7;padding:6px;text-align:center;font:12px system-ui'
      document.body.prepend(banner)
    })
  }, `TESTE LOCAL — ${mode}; dados sintéticos, sem gravação homologada`)
  context.on('page', page => {
    page.on('pageerror', () => pageErrors++)
    page.on('console', msg => { if ([secret,username,process.env.CENTRAL_UI_PASSWORD].filter(Boolean).some(value => msg.text().includes(value))) leaks++ })
    page.on('response', response => {
      const url = new URL(response.url()); if (!url.pathname.startsWith('/api/')) return
      responses.push({ method: response.request().method(), path: url.pathname.replace(/[0-9a-f-]{36}/g, '{id}'), status: response.status() })
      if (!url.pathname.endsWith('/csrf')) responseTasks.push(response.text().then(text => {
        if ([secret,username,process.env.CENTRAL_UI_PASSWORD].filter(Boolean).some(value => text.includes(value))) leaks++
      }).catch(() => {}))
    })
  })
}
async function safe(page) {
  const body = await page.locator('body').innerText()
  assert.ok(!body.includes(secret) && !body.includes(username) && !body.includes('synthetic-csrf'))
  assert.equal(await page.evaluate(() => localStorage.length + sessionStorage.length), 0)
}
async function mockContext(role = 'Admin') {
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 1000 }, locale: 'pt-BR', timezoneId: 'America/Sao_Paulo' })
  await watch(context)
  const proxy = { id: randomUUID(), host: 'browser.profile.test', port: 8080, region: 'BR', status: 'Active', usernameMasked: '***', available: true, expiresAt: '2099-01-01T12:00:00Z', createdAt: '2026-10-11T00:00:00Z', updatedAt: '2026-10-11T00:00:00Z' }
  const state = { session: true, readStatus: 200, writeStatus: 200, writes: [], allocations: [], profiles: Array.from({ length: 32 }, (_, i) => ({ id: randomUUID(), name: `Perfil sintético ${String(i+1).padStart(2,'0')}`, platform: 'Sintética', status: 'Active', createdAt: `2026-10-11T00:00:${String(i).padStart(2,'0')}Z`, updatedAt: '2026-10-11T00:01:00Z', closedAt: null })) }
  await context.route(url => url.pathname.startsWith('/api/'), async route => {
    const incoming = route.request(), url = new URL(incoming.url()), endpoint = url.pathname, method = incoming.method()
    const reply = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: status === 204 ? '' : JSON.stringify(body) })
    if (endpoint.endsWith('/csrf')) return reply({ token: 'synthetic-csrf' })
    if (endpoint.endsWith('/me')) return state.session ? reply({ id: 'synthetic-'+role, email: role.toLowerCase()+'@example.test', roles: [role] }) : reply({},401)
    if (method === 'POST') {
      assert.equal(incoming.headers()['x-csrf-token'],'synthetic-csrf')
      if (endpoint.endsWith('/logout')) { state.session=false; return reply({},204) }
      if (role === 'Operator') return reply({},403)
      state.writes.push(endpoint)
      if (state.writeStatus !== 200) return reply({},state.writeStatus)
      const body = incoming.postDataJSON()
      if (endpoint === '/api/v1/profiles') { const profile={...state.profiles[0],id:randomUUID(),name:body.name,platform:body.platform};state.profiles.push(profile);return reply(profile,201) }
      if (endpoint.endsWith('/reserve')) {
        assert.deepEqual(Object.keys(body).sort(),['profileId','proxyId']);assert.match(incoming.headers()['idempotency-key'],/^[0-9a-f-]{36}$/)
        const allocation={id:randomUUID(),...body,status:'Reserved',createdAt:new Date().toISOString(),updatedAt:new Date().toISOString(),reservationExpiresAt:new Date(Date.now()+900000).toISOString(),confirmedAt:null,endedAt:null,endReason:null}
        state.allocations.push(allocation);return reply(allocation,201)
      }
      const allocation=state.allocations.find(item=>endpoint.includes(item.id));assert.ok(allocation)
      if(endpoint.endsWith('/confirm')){allocation.status='Confirmed';allocation.confirmedAt=new Date().toISOString()}
      if(endpoint.endsWith('/release')){allocation.status='Released';allocation.endedAt=new Date().toISOString();allocation.endReason='Released'}
      return reply({},204)
    }
    if(state.readStatus!==200)return reply({},state.readStatus)
    const page=Number(url.searchParams.get('page')||1),pageSize=Number(url.searchParams.get('pageSize')||20)
    const paged=rows=>reply({items:rows.slice((page-1)*pageSize,page*pageSize),page,pageSize,total:rows.length})
    if(endpoint==='/api/v1/allocations')return paged(state.allocations.filter(item=>item.profileId===url.searchParams.get('profileId')&&item.status===url.searchParams.get('status')))
    if(/\/profiles\/[^/]+\/allocations$/.test(endpoint))return paged(state.allocations.filter(item=>item.profileId===endpoint.split('/')[4]&&(!url.searchParams.has('status')||item.status===url.searchParams.get('status'))))
    if(endpoint==='/api/v1/profiles'){
      let rows=state.profiles.filter(item=>['name','platform','status'].every(field=>!url.searchParams.has(field)||item[field]===url.searchParams.get(field)))
      const sort=url.searchParams.get('sort')||'createdAt',direction=url.searchParams.get('direction')==='asc'?1:-1
      rows=[...rows].sort((a,b)=>(a[sort]<b[sort]?-1:a[sort]>b[sort]?1:0)*direction);return paged(rows)
    }
    if(endpoint.startsWith('/api/v1/profiles/'))return reply(state.profiles.find(item=>endpoint.endsWith(item.id)))
    const occupied=state.allocations.some(item=>item.status==='Confirmed'||item.status==='Reserved'&&Date.parse(item.reservationExpiresAt)>Date.now())
    if(endpoint==='/api/v1/proxies/available')return paged(occupied?[]:[proxy])
    if(endpoint.startsWith('/api/v1/proxies/'))return reply({...proxy,available:!occupied})
    return reply({},404)
  })
  return { context, page: await context.newPage(), state }
}
async function flow(page, name = 'Perfil navegador sintético') {
  stage='cadastro';await page.getByRole('button',{name:'Novo perfil',exact:true}).click()
  const modal=page.getByRole('dialog');await modal.getByLabel('Nome do perfil').fill(name);await modal.getByLabel('Plataforma',{exact:true}).fill('Sintética')
  await modal.getByRole('button',{name:'Cadastrar perfil',exact:true}).click();await page.getByRole('heading',{name,exact:true}).waitFor();check('Cadastro e detalhes')
  stage='reserva';await page.getByRole('button',{name:'Reservar proxy',exact:true}).click();await page.getByRole('radio',{name:'Selecionar browser.profile.test:8080'}).check()
  await page.getByRole('button',{name:'Reservar proxy selecionada'}).click();await page.getByText('Proxy reservada. Confira o prazo antes de confirmar.').waitFor();await page.getByRole('button',{name:'Confirmar reserva'}).waitFor();await screenshot(page,'reserva-desktop');check('Seleção disponível e prazo da reserva')
  stage='confirmação';await page.getByRole('button',{name:'Confirmar reserva'}).click();await page.getByText('Reserva confirmada.',{exact:true}).waitFor();check('Confirmação e atualização do histórico')
  await page.getByRole('button',{name:'Liberar proxy'}).click();await page.getByRole('dialog').getByRole('button',{name:'Cancelar',exact:true}).click();check('Cancelamento da liberação sem escrita')
  await page.getByRole('button',{name:'Liberar proxy'}).click();await screenshot(page,'confirmacao-liberacao');await page.getByRole('dialog').getByRole('button',{name:'Confirmar liberação'}).click()
  await page.getByText('Alocação liberada. A API reavaliará a disponibilidade da proxy.').waitFor();await page.getByRole('region',{name:'Tabela de histórico'}).getByRole('cell',{name:'Liberada',exact:true}).waitFor();check('Liberação explícita e histórico preservado')
  await page.getByRole('button',{name:'Reservar proxy',exact:true}).click();await page.getByRole('radio').check();await page.getByRole('button',{name:'Reservar proxy selecionada'}).click();await page.getByRole('button',{name:'Confirmar reserva'}).waitFor();check('Reutilização da proxy liberada')
  await safe(page)
}
async function mocks() {
  const {context,page,state}=await mockContext();stage='listagem';await page.goto(origin+'/profiles');await page.locator('tbody tr').first().waitFor();await screenshot(page,'listagem-desktop');check('Listagem paginada no layout existente')
  await page.getByRole('button',{name:'Próxima página de perfis'}).click();await page.getByText(/Página 2 de 2/).waitFor();check('Paginação de perfis')
  await page.getByLabel('Nome exato').fill('Perfil sintético 01');await page.getByRole('button',{name:'Aplicar filtros'}).click();await page.getByRole('link',{name:'Perfil sintético 01',exact:true}).waitFor();check('Filtro exato suportado pela API')
  await flow(page)
  state.writeStatus=409;stage='conflito';const count=state.writes.length;await page.getByRole('button',{name:'Confirmar reserva'}).click();await page.getByText(/Conflito operacional/).waitFor();assert.equal(state.writes.length,count+1);await screenshot(page,'conflito');check('Conflito 409 sem retry automático')
  state.writeStatus=200;state.allocations.at(-1).reservationExpiresAt=new Date(Date.now()-1000).toISOString();await page.getByRole('button',{name:'Atualizar dados'}).click();await page.getByText('Reserva com prazo vencido',{exact:true}).waitFor();assert.equal(await page.getByRole('button',{name:'Confirmar reserva'}).count(),0);check('Expiração sem confirmação automática')
  await page.setViewportSize({width:390,height:844});await screenshot(page,'detalhes-mobile');assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));check('Detalhes responsivos sem overflow da página')
  state.readStatus=503;await page.getByRole('button',{name:'Atualizar dados'}).click();await page.getByText(/Serviço indisponível/).first().waitFor();check('Erro de serviço exibido')
  await context.close()
  for(const role of ['Operator','Manager']){
    const test=await mockContext(role);await test.page.goto(origin+'/profiles');await test.page.getByRole('heading',{name:'Perfis operacionais'}).waitFor();assert.equal(await test.page.getByRole('button',{name:'Novo perfil'}).count(),role==='Manager'?1:0)
    if(role==='Operator'){await test.page.getByRole('link',{name:test.state.profiles.at(-1).name,exact:true}).click();await test.page.getByRole('heading',{name:'Histórico de alocações'}).waitFor();assert.equal(await test.page.getByRole('button',{name:'Reservar proxy',exact:true}).count(),0);await screenshot(test.page,'operator-consulta')}
    await safe(test.page);await test.context.close();check('Permissão '+role)
  }
}
async function actual() {
  assert.equal(process.env.CENTRAL_UI_API_ORIGIN,'https://localhost:7091');assert.match(process.env.CENTRAL_UI_DATABASE||'',/^p6_ui_test_[a-f0-9]{32}$/)
  const context=await browser.newContext({ignoreHTTPSErrors:true,viewport:{width:1440,height:1000},locale:'pt-BR',timezoneId:'America/Sao_Paulo'});await watch(context)
  const page=await context.newPage();await page.goto(origin+'/profiles');await page.getByLabel('E-mail').fill('admin-ui@example.test');await page.getByLabel('Senha',{exact:true}).fill(process.env.CENTRAL_UI_PASSWORD);await page.getByRole('button',{name:'Entrar',exact:true}).click();await page.getByRole('heading',{name:'Início',exact:true}).waitFor();check('Login real na API descartável')
  const csrf=await (await context.request.get(origin+'/api/v1/auth/csrf')).json()
  const seeded=await context.request.post(origin+'/api/v1/proxies',{headers:{'X-CSRF-TOKEN':csrf.token},data:{host:'browser.profile.test',port:8080,username,password:secret,region:'BR',status:'Active',expiresAt:'2099-01-01T12:00:00Z'}});assert.equal(seeded.status(),201)
  await page.getByRole('link',{name:'Perfis',exact:true}).click();await page.getByText('Nenhum perfil encontrado.',{exact:true}).waitFor();await flow(page,'Perfil Chrome API sintético')
  const cookie=(await context.cookies()).find(value=>value.name==='__Host-Central.Session');assert.ok(cookie&&cookie.secure&&cookie.httpOnly&&cookie.sameSite==='Strict');check('Cookie seguro e CSRF reutilizados')
  const history=(await context.request.get(origin+'/api/v1/allocations')).ok();assert.ok(history)
  await safe(page);await context.close()
}
(async()=>{
  fs.mkdirSync(artifacts,{recursive:true})
  if(real){
    const health=await request.newContext({ignoreHTTPSErrors:true});let healthy=false
    for(let i=0;i<30&&!healthy;i++){try{healthy=(await health.get('https://localhost:7091/health')).ok()}catch{}if(!healthy)await new Promise(resolve=>setTimeout(resolve,300))}assert.ok(healthy);await health.dispose()
    const {createServer}=await import(pathToFileURL(require.resolve('../src/central-contingencia-web/node_modules/vite')).href)
    vite=await createServer({configFile:path.join(root,'src/central-contingencia-web/vite.config.ts'),root:path.join(root,'src/central-contingencia-web'),server:{host:'localhost',port:5174,strictPort:true,proxy:{'/api':{target:'https://localhost:7091',secure:false}}}});await vite.listen()
  }
  browser=await chromium.launch({executablePath:process.env.CHROME_PATH||'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true})
  if(real)await actual();else await mocks()
  await Promise.all(responseTasks);assert.equal(pageErrors,0);assert.equal(leaks,0)
  fs.writeFileSync(path.join(artifacts,`${mode}-results.json`),JSON.stringify({mode,passed:true,checks,pageErrors,leaks,responses},null,2));console.log(`Playwright Chrome ${mode}: ${checks.length} verificações aprovadas, sem erros de página ou vazamentos.`)
})().catch(async error=>{fs.mkdirSync(artifacts,{recursive:true});const location=error?.stack?.split('\n').find(line=>line.trim().startsWith('at ')&&line.includes('Verify-ProfileUi.cjs:'));fs.writeFileSync(path.join(artifacts,`${mode}-results.json`),JSON.stringify({mode,passed:false,failedStage:stage,errorType:error?.name,location,checks,pageErrors,leaks,responses},null,2));console.error('Verificação falhou no cenário '+stage+'; detalhes sensíveis omitidos.');process.exitCode=1}).finally(async()=>{if(browser)await browser.close();if(vite)await vite.close()})
