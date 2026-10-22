export function gridReturn(state: unknown) {
  if (state && typeof state === 'object' && 'returnTo' in state && typeof state.returnTo === 'string') {
    try {
      const url = new URL(state.returnTo, 'https://local.invalid')
      if (url.origin === 'https://local.invalid' && url.pathname === '/proxies') return url.pathname + url.search
    } catch { /* Use the main grid for invalid navigation state. */ }
  }
  return '/proxies'
}
