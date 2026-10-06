import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createBrowserRouter } from 'react-router'
import { App } from './app/App'
import { createQueryClient } from './app/queryClient'
import { routes } from './app/routes'
import './styles.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App queryClient={createQueryClient()} router={createBrowserRouter(routes)} />
  </StrictMode>,
)
