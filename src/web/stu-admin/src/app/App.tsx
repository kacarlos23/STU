import { AuthGate } from '@stu/shared'
import { AdminDashboard } from '../features/dashboard/AdminDashboard'
import './app.css'

export function App() {
  return (
    <AuthGate portal="admin" requiredRole="GlobalAdministrator">
      {(context) => <AdminDashboard {...context} />}
    </AuthGate>
  )
}
