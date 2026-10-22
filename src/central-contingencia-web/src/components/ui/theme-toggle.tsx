import { useEffect, useState } from 'react'
import { Moon, Sun } from 'lucide-react'
import { applyTheme, saveTheme, storedTheme, systemTheme, type Theme } from '../../lib/theme'
import { Button } from './button'
// Segue o sistema até a primeira escolha manual; depois a escolha fica salva neste navegador.
export function ThemeToggle({ className }: { className?: string }) {
  const [theme, setTheme] = useState<Theme>(() => storedTheme() ?? systemTheme())
  useEffect(() => { applyTheme(theme) }, [theme])
  useEffect(() => {
    if (storedTheme() || typeof window.matchMedia !== 'function') return
    const query = window.matchMedia('(prefers-color-scheme: dark)')
    const follow = () => { if (!storedTheme()) setTheme(query.matches ? 'dark' : 'light') }
    query.addEventListener('change', follow); return () => query.removeEventListener('change', follow)
  }, [])
  const dark = theme === 'dark'
  return <Button type="button" variant="outline" className={className ?? 'gap-2 px-3 text-sm'} aria-label={dark ? 'Ativar modo claro' : 'Ativar modo escuro'} title={dark ? 'Modo claro' : 'Modo escuro'}
    onClick={() => { const next: Theme = dark ? 'light' : 'dark'; saveTheme(next); setTheme(next) }}>{dark ? <Sun size={16} /> : <Moon size={16} />}</Button>
}
