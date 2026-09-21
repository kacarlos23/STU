import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles/global.css'
import { App } from './app/App'
import './styles/purple-theme.css'

document.documentElement.dataset.stuRelease = 'security-validation-2026-08-24'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
