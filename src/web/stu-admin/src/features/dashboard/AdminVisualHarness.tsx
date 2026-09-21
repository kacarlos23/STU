import { createRoot } from 'react-dom/client'
import type { Session } from '@stu/shared'
import { AdminDashboard } from './AdminDashboard'
import '../../styles/global.css'
import '../../app/app.css'
import '../../styles/purple-theme.css'

if (!import.meta.env.DEV || !['localhost', '127.0.0.1'].includes(location.hostname)) {
  throw new Error('Somente validação local')
}

const session: Session = {
  id: 'visual-admin',
  userName: 'admin.visual',
  displayName: 'Administrador global',
  mustChangePassword: false,
  healthUnit: null,
  roles: [{ name: 'GlobalAdministrator', displayName: 'Administrador global' }],
  permissions: ['*'],
}

const originalFetch = window.fetch.bind(window)
window.fetch = async (input, init) => {
  const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
  if (url.startsWith('/api/admin/overview')) {
    return Response.json({ activeHealthUnits: 12, activeUsers: 184, pendingPasswordChanges: 7, activeRoles: 6 })
  }
  if (url.startsWith('/api/notifications')) return Response.json([])
  return originalFetch(input, init)
}

createRoot(document.getElementById('root')!).render(
  <AdminDashboard session={session} logout={async () => {}} />,
)
