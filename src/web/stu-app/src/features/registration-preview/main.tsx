import { createRoot } from 'react-dom/client'
import { RegistrationPreview } from './RegistrationPreview'
import '../../styles/global.css'
import '../../app/app.css'
import '../../styles/purple-theme.css'
import './registration-preview.css'

// Synthetic preview: separate development entry, no API calls or persistent storage.
if (!import.meta.env.DEV || !['localhost', '127.0.0.1'].includes(location.hostname)) {
  throw new Error('Esta prévia está disponível somente no ambiente local de desenvolvimento.')
}

createRoot(document.getElementById('root')!).render(<RegistrationPreview />)
