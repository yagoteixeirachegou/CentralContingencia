import { Fragment } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { ChevronRight } from 'lucide-react'
import { gridReturn } from '../../features/proxies/grid/navigation'
import { trailFor } from './trail'

export function Breadcrumbs() {
  const location = useLocation()
  const trail = trailFor(location.pathname, gridReturn(location.state))
  return <nav aria-label="Trilha de navegação">
    <ol className="flex flex-wrap items-center gap-1.5 text-sm">
      {trail.map((crumb, index) => <Fragment key={crumb.label + index}>
        {index > 0 && <li aria-hidden className="text-slate-400"><ChevronRight size={14} /></li>}
        <li>{crumb.to
          ? <Link to={crumb.to} state={location.state} className="text-link hover:underline">{crumb.label}</Link>
          : <span aria-current={index === trail.length - 1 ? 'page' : undefined} className={index === trail.length - 1 ? 'font-medium text-slate-900' : 'text-slate-600'}>{crumb.label}</span>}</li>
      </Fragment>)}
    </ol>
  </nav>
}
