export type Theme = 'light' | 'dark'
const key = 'central-theme'
export function storedTheme(): Theme | null {
  try { const value = localStorage.getItem(key); return value === 'light' || value === 'dark' ? value : null } catch { return null }
}
export function systemTheme(): Theme {
  return typeof window.matchMedia === 'function' && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}
export function applyTheme(theme: Theme) { document.documentElement.classList.toggle('dark', theme === 'dark') }
export function saveTheme(theme: Theme) { try { localStorage.setItem(key, theme) } catch { /* Preferência só vale nesta sessão. */ } }
