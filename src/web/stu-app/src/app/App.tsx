import { AuthGate } from '@stu/shared'
import { Dashboard } from '../features/dashboard/Dashboard'
import './app.css'

export function App() {
  return (
    <AuthGate portal="main">
      {(context) => <Dashboard {...context} />}
    </AuthGate>
  )
}
