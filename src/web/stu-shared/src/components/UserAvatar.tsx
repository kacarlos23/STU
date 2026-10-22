import { useState } from 'react'

export function UserAvatar({ name, src }: { name: string; src?: string | null }) {
  const [failed, setFailed] = useState<string | null>(null)
  return <span aria-hidden="true" className="avatar">{src && failed !== src ? <img alt="" onError={() => setFailed(src)} src={src} /> : name.trim().split(/\s+/).slice(0, 2).map(part => part[0]?.toUpperCase()).join('')}</span>
}
