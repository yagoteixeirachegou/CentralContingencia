// Submenu sem `to` ainda não tem tela e aparece desativado, como "Em breve".
export interface SheetChild { id: string; label: string; to?: string; active?(pathname: string): boolean }
export interface Sheet { id: string; label: string; to?: string; children?: readonly SheetChild[] }
// Abas da planilha original. Abas sem `to` nem `children` ainda não foram migradas e aparecem como "Em breve".
// A aba HTTP - FACEBOOK se divide em submenus; `active` decide qual deles acende (a ingestão de TXT pertence a Perfis).
export const sheets: readonly Sheet[] = [
  { id: 'http-facebook', label: 'HTTP - FACEBOOK', to: '/proxies', children: [
    { id: 'proxies', label: 'Proxies', to: '/proxies', active: path => path === '/proxies' || path === '/proxies/import' },
    { id: 'perfis', label: 'Perfis', to: '/profiles', active: path => path.startsWith('/profiles') || path === '/proxies/ingestion' },
    { id: 'aquecimento', label: 'Aquecimento', to: '/warmup' },
  ] },
  { id: 'http-tiktok', label: 'HTTP - TIKTOK' },
  { id: 'http-google-ads', label: 'HTTP - GOOGLE ADS' },
  { id: 'numeros-operacao', label: 'NUMEROS - OPERAÇAO' },
  { id: 'numeros-facebook', label: 'NUMEROS - FACEBOOK' },
]
export const httpFacebookSheet = sheets[0]
