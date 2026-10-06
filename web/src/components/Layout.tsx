import { LayoutDashboard, Menu, Moon, Package, PanelLeftClose, PanelLeftOpen, Plus, Sun, Users, X } from 'lucide-react'
import { useState, type ComponentType } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigation } from 'react-router'
import { Button } from '@/components/ui/button'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { useTheme } from '@/lib/theme'
import { cn } from '@/lib/utils'

interface NavItem {
  to: string
  label: string
  icon: ComponentType<{ className?: string }>
}

const NAV: NavItem[] = [
  { to: '/dashboard', label: 'Dashboard', icon: LayoutDashboard },
  { to: '/orders', label: 'Orders', icon: Package },
  { to: '/customers', label: 'Customers', icon: Users },
]

const COLLAPSED_KEY = 'om-sidebar-collapsed'

export function Layout() {
  const [collapsed, setCollapsed] = useState(() => {
    try {
      return localStorage.getItem(COLLAPSED_KEY) === '1'
    } catch {
      return false
    }
  })
  const [mobileOpen, setMobileOpen] = useState(false)

  const toggleCollapsed = () =>
    setCollapsed((c) => {
      try {
        localStorage.setItem(COLLAPSED_KEY, c ? '0' : '1')
      } catch {
        // Not persisting is fine.
      }
      return !c
    })

  return (
    <div className="flex min-h-svh bg-background">
      <a
        href="#main"
        className="sr-only z-50 rounded-md bg-background px-4 py-2 focus:not-sr-only focus:fixed focus:top-3 focus:left-3"
      >
        Skip to content
      </a>

      {/* Desktop sidebar */}
      <aside
        className={cn(
          'sticky top-0 hidden h-svh shrink-0 flex-col border-r border-sidebar-border bg-sidebar text-sidebar-foreground transition-[width] duration-200 lg:flex',
          collapsed ? 'w-[68px]' : 'w-64',
        )}
      >
        <SidebarContent collapsed={collapsed} />
        <div className="border-t border-sidebar-border p-3">
          <Button
            variant="ghost"
            size={collapsed ? 'icon' : 'sm'}
            onClick={toggleCollapsed}
            className="w-full justify-start text-sidebar-muted hover:bg-sidebar-accent hover:text-sidebar-accent-foreground"
            aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
          >
            {collapsed ? <PanelLeftOpen /> : <PanelLeftClose />}
            {!collapsed && <span>Collapse</span>}
          </Button>
        </div>
      </aside>

      {/* Mobile drawer */}
      {mobileOpen && (
        <div className="fixed inset-0 z-40 lg:hidden">
          <button
            type="button"
            aria-label="Close navigation"
            className="absolute inset-0 bg-black/50 backdrop-blur-sm"
            onClick={() => setMobileOpen(false)}
          />
          <aside className="relative flex h-full w-72 flex-col bg-sidebar text-sidebar-foreground shadow-xl animate-in slide-in-from-left">
            <Button
              variant="ghost"
              size="icon"
              className="absolute top-4 right-3 text-sidebar-muted hover:bg-sidebar-accent hover:text-sidebar-accent-foreground"
              onClick={() => setMobileOpen(false)}
              aria-label="Close navigation"
            >
              <X />
            </Button>
            <SidebarContent collapsed={false} onNavigate={() => setMobileOpen(false)} />
          </aside>
        </div>
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        <NavigationProgress />
        <TopBar onOpenNav={() => setMobileOpen(true)} />
        <main id="main" tabIndex={-1} className="mx-auto w-full max-w-7xl flex-1 px-4 py-6 outline-none sm:px-6 lg:px-8 lg:py-8">
          <Outlet />
        </main>
      </div>
    </div>
  )
}

/** `onNavigate` closes the mobile drawer when a link is followed. */
/** A thin bar across the top while the next page's code loads (pages are lazy chunks). */
function NavigationProgress() {
  const loading = useNavigation().state === 'loading'
  return (
    <div
      aria-hidden="true"
      className={cn(
        'pointer-events-none fixed inset-x-0 top-0 z-50 h-0.5 origin-left bg-primary transition-[transform,opacity] duration-500',
        loading ? 'scale-x-75 opacity-100' : 'scale-x-100 opacity-0',
      )}
    />
  )
}

function SidebarContent({ collapsed, onNavigate }: { collapsed: boolean; onNavigate?: () => void }) {
  return (
    <>
      <div className={cn('flex h-16 items-center gap-3 px-4', collapsed && 'justify-center px-0')}>
        <Logo />
        {!collapsed && (
          <div className="leading-tight">
            <p className="text-sm font-semibold tracking-tight text-sidebar-accent-foreground">Amrod Orders</p>
            <p className="text-xs text-sidebar-muted">SADC order management</p>
          </div>
        )}
      </div>

      <div className={cn('px-3 pb-2', collapsed && 'px-2')}>
        <SidebarLink
          item={{ to: '/orders/new', label: 'New order', icon: Plus }}
          collapsed={collapsed}
          onNavigate={onNavigate}
          className="bg-sidebar-primary text-sidebar-primary-foreground hover:bg-sidebar-primary/90 hover:text-sidebar-primary-foreground"
          end
        />
      </div>

      <nav aria-label="Main" className={cn('flex flex-1 flex-col gap-1 px-3 py-2', collapsed && 'px-2')}>
        {!collapsed && <p className="px-3 pt-2 pb-1 text-[11px] font-medium tracking-wider text-sidebar-muted uppercase">Workspace</p>}
        {NAV.map((item) => (
          <SidebarLink key={item.to} item={item} collapsed={collapsed} onNavigate={onNavigate} />
        ))}
      </nav>
    </>
  )
}

function SidebarLink({
  item,
  collapsed,
  className,
  end,
  onNavigate,
}: {
  item: NavItem
  collapsed: boolean
  className?: string
  end?: boolean
  onNavigate?: () => void
}) {
  const Icon = item.icon
  const link = (
    <NavLink
      to={item.to}
      end={end ?? false}
      onClick={onNavigate}
      aria-label={collapsed ? item.label : undefined}
      className={({ isActive }) =>
        cn(
          'flex h-9 items-center gap-3 rounded-md px-3 text-sm font-medium text-sidebar-muted transition-colors hover:bg-sidebar-accent hover:text-sidebar-accent-foreground',
          collapsed && 'justify-center px-0',
          isActive && !className && 'bg-sidebar-accent text-sidebar-accent-foreground',
          className,
        )
      }
    >
      <Icon className="size-4 shrink-0" />
      {!collapsed && <span>{item.label}</span>}
    </NavLink>
  )
  if (!collapsed) return link
  return (
    <Tooltip>
      <TooltipTrigger asChild>{link}</TooltipTrigger>
      <TooltipContent side="right">{item.label}</TooltipContent>
    </Tooltip>
  )
}

function TopBar({ onOpenNav }: { onOpenNav(): void }) {
  const { theme, toggle } = useTheme()
  return (
    <header className="sticky top-0 z-30 flex h-16 items-center gap-3 border-b bg-background/80 px-4 backdrop-blur supports-[backdrop-filter]:bg-background/60 sm:px-6 lg:px-8">
      <Button variant="ghost" size="icon" className="lg:hidden" onClick={onOpenNav} aria-label="Open navigation">
        <Menu />
      </Button>
      <Breadcrumbs />
      <div className="ml-auto flex items-center gap-2">
        <Button
          variant="ghost"
          size="icon"
          onClick={toggle}
          aria-label={theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
        >
          {theme === 'dark' ? <Sun /> : <Moon />}
        </Button>
        <div className="flex items-center gap-2.5 rounded-full border bg-card py-1 pr-3 pl-1">
          <span
            aria-hidden="true"
            className="flex size-7 items-center justify-center rounded-full bg-primary text-[11px] font-semibold text-primary-foreground"
          >
            WD
          </span>
          <span className="hidden text-sm leading-tight sm:block">
            <span className="block font-medium">Web Developer</span>
            <span className="block text-xs text-muted-foreground">Dev sign-in · Admin</span>
          </span>
        </div>
      </div>
    </header>
  )
}

const SECTION_LABELS: Record<string, string> = {
  dashboard: 'Dashboard',
  orders: 'Orders',
  customers: 'Customers',
  new: 'New',
}

function Breadcrumbs() {
  const { pathname } = useLocation()
  const parts = pathname.split('/').filter(Boolean)
  return (
    <nav aria-label="Breadcrumb" className="min-w-0">
      <ol className="flex items-center gap-1.5 text-sm text-muted-foreground">
        {parts.map((part, i) => {
          const href = `/${parts.slice(0, i + 1).join('/')}`
          const last = i === parts.length - 1
          const label = SECTION_LABELS[part] ?? `#${part.slice(0, 8)}`
          return (
            <li key={href} className="flex min-w-0 items-center gap-1.5">
              {i > 0 && <span aria-hidden="true">/</span>}
              {last ? (
                <span aria-current="page" className="truncate font-medium text-foreground">
                  {label}
                </span>
              ) : (
                <Link to={href} className="truncate hover:text-foreground">
                  {label}
                </Link>
              )}
            </li>
          )
        })}
      </ol>
    </nav>
  )
}

function Logo() {
  return (
    <span className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-gradient-to-br from-sidebar-primary to-chart-4 shadow-lg shadow-sidebar-primary/20">
      <Package className="size-[18px] text-white" aria-hidden="true" />
    </span>
  )
}
