// Ilustração decorativa da regra central do sistema: cada perfil (esquerda) usa uma única proxy (direita).
// Os pares são abstratos, não representam dados reais.
const rows = 7
const pairs = [3, 1, 0, 4, 6, 2, 5] // permutação: cada proxy aparece exatamente uma vez
const active = 3
const y = (index: number) => 36 + index * 48
export function LoginArt({ className }: { className?: string }) {
  return <svg viewBox="0 0 480 372" className={className} aria-hidden="true" focusable="false" fill="none">
    <g strokeLinecap="round">
      {pairs.map((target, index) => <path key={index} className="pair-line" pathLength={1} data-active={index === active ? '' : undefined}
        d={`M 64 ${y(index)} C 220 ${y(index)}, 260 ${y(target)}, 416 ${y(target)}`}
        stroke={index === active ? '#f5b544' : '#7dd3fc'} strokeOpacity={index === active ? 1 : 0.38} strokeWidth={index === active ? 2.5 : 1.5} />)}
    </g>
    {Array.from({ length: rows }, (_, index) => {
      const on = index === active
      return <g key={index}>
        <circle cx="52" cy={y(index)} r="9" fill={on ? '#f5b544' : 'var(--color-ink)'} stroke={on ? '#f5b544' : '#7dd3fc'} strokeOpacity={on ? 1 : 0.6} strokeWidth="1.5" />
        <rect x="414" y={y(pairs[index]) - 9} width="28" height="18" rx="4" fill={on ? '#f5b544' : 'var(--color-ink)'} stroke={on ? '#f5b544' : '#7dd3fc'} strokeOpacity={on ? 1 : 0.6} strokeWidth="1.5" />
      </g>
    })}
  </svg>
}
