import { RegistrationFlow } from '../registration/RegistrationFlow'
import { exampleData } from './data'

export function RegistrationPreview() {
  return <RegistrationFlow initialData={exampleData()} demo />
}
