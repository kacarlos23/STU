import { useEffect, useRef, useState, type FormEvent } from 'react'
import { ApiError, getAccountSettings, requestPasswordReset, saveAccountSettings, type AccountSettings } from '../auth/api'
import { useUiActions, useUnsavedChanges } from '../interaction/InteractionProvider'
import './account-settings.css'

export type { AccountSettings } from '../auth/api'

export function AccountSettingsWorkspace({ onSaved }: { onSaved: (profile: AccountSettings) => void }) {
  const { guard } = useUiActions()
  const [profile, setProfile] = useState<AccountSettings | null>(null)
  const [name, setName] = useState('')
  const [appearance, setAppearance] = useState<'light' | 'dark'>('light')
  const [photo, setPhoto] = useState<string | null>(null)
  const [removePhoto, setRemovePhoto] = useState(false)
  const [busy, setBusy] = useState(false)
  const [requesting, setRequesting] = useState(false)
  const [photoBusy, setPhotoBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<Record<string, string[]>>({})
  const [notice, setNotice] = useState<string | null>(null)
  const [online, setOnline] = useState(navigator.onLine)
  const fileInput = useRef<HTMLInputElement>(null)
  const dirty = Boolean(profile && (name !== profile.displayName || appearance !== profile.appearance || photo || removePhoto && profile.avatarUrl))
  useUnsavedChanges(dirty || busy || requesting || photoBusy)

  useEffect(() => { let active = true; getAccountSettings().then(value => { if (active) reset(value) }).catch(caught => { if (active) showError(caught) }); return () => { active = false } }, [])
  useEffect(() => { const update = () => setOnline(navigator.onLine); window.addEventListener('online', update); window.addEventListener('offline', update); return () => { window.removeEventListener('online', update); window.removeEventListener('offline', update) } }, [])

  function reset(value: AccountSettings) { setProfile(value); setName(value.displayName); setAppearance(value.appearance); setPhoto(null); setRemovePhoto(false); setFields({}); setError(null); if (fileInput.current) fileInput.current.value = '' }
  function showError(caught: unknown) { setError(caught instanceof Error ? caught.message : 'Não foi possível carregar as configurações.'); setFields(caught instanceof ApiError ? caught.validationErrors : {}) }
  async function reload() { setBusy(true); try { reset(await getAccountSettings()); setNotice(null) } catch (caught) { showError(caught) } finally { setBusy(false) } }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (!profile || !online || busy || photoBusy) return
    setBusy(true); setError(null); setFields({}); setNotice(null)
    try { const value = await saveAccountSettings({ displayName: name.trim(), appearance, concurrencyToken: profile.concurrencyToken, photo, removePhoto }); reset(value); onSaved(value); setNotice('Configurações salvas.') } catch (caught) { showError(caught) } finally { setBusy(false) }
  }
  async function choosePhoto(file: File | undefined) {
    if (!file) return
    setPhotoBusy(true); setError(null); setFields({})
    try { setPhoto(await normalizedPhoto(file)); setRemovePhoto(false); setNotice(null) } catch (caught) { showError(caught) } finally { setPhotoBusy(false) }
  }
  async function sendRequest() {
    if (!online || requesting || profile?.passwordResetRequestedAtUtc) return
    setRequesting(true); setError(null); setNotice(null)
    try { const value = await requestPasswordReset(); setProfile(value); setNotice('Pedido enviado ao responsável. Aguarde o atendimento para receber a senha temporária.') } catch (caught) { showError(caught) } finally { setRequesting(false) }
  }

  if (!profile) return <section className="account-settings"><h2>Configurações da sua conta</h2>{error ? <div role="alert"><p>{error}</p><button disabled={busy || !online} onClick={() => void reload()} type="button">Tentar novamente</button></div> : <p role="status">Carregando configurações…</p>}</section>
  const preview = removePhoto ? null : photo ? `data:image/png;base64,${photo}` : profile.avatarUrl
  return <section className="account-settings" aria-label="Configurações da conta">
    {error && <div className="account-settings-error" role="alert"><p>{error}</p><button disabled={busy} onClick={() => void guard(() => { void reload() })} type="button">Recarregar configurações</button></div>}
    {notice && <p className="account-settings-notice" role="status">{notice}</p>}
    {!online && <p className="account-settings-error" role="status">Conecte-se para salvar alterações ou enviar um pedido.</p>}
    <form onSubmit={event => void save(event)}>
      <div className="account-settings-grid">
        <section className="account-settings-card"><span className="account-settings-kicker">Sua conta</span><h2>Meu perfil</h2><p>Atualize como seu nome e sua foto aparecem no sistema.</p>
          <div className="account-photo-editor"><span className="account-photo-preview" aria-hidden="true">{preview ? <img src={preview} alt="" /> : name.trim().split(/\s+/).slice(0, 2).map(part => part[0]?.toUpperCase()).join('')}</span><div><label className="account-photo-picker">{photoBusy ? 'Preparando foto…' : 'Escolher foto'}<input accept="image/png,image/jpeg,image/webp" aria-label="Foto de perfil" disabled={busy || photoBusy || !online} onChange={event => void choosePhoto(event.target.files?.[0])} ref={fileInput} type="file" /></label><small>PNG, JPG ou WebP, até 5 MB.</small>{(preview || photo) && <button disabled={busy || photoBusy} onClick={() => { setPhoto(null); setRemovePhoto(true); if (fileInput.current) fileInput.current.value = '' }} type="button">Remover foto</button>}</div></div>
          {fields.photo && <p className="account-field-error">{fields.photo[0]}</p>}
          <label className="account-field">Nome completo<input aria-invalid={Boolean(fields.displayName)} autoComplete="name" maxLength={160} onChange={event => setName(event.target.value)} required value={name} /></label>{fields.displayName && <p className="account-field-error">{fields.displayName[0]}</p>}
          <label className="account-field">Usuário de acesso<input autoComplete="username" readOnly value={profile.userName} /></label><small>O usuário de acesso é administrado pelo responsável da UBS.</small>
        </section>
        <section className="account-settings-card"><span className="account-settings-kicker">Preferências</span><h2>Aparência</h2><p>Escolha a preferência que deseja manter na sua conta.</p><fieldset className="account-theme-options"><legend className="sr-only">Modo de aparência</legend>{(['light', 'dark'] as const).map(value => <label className={appearance === value ? 'is-selected' : ''} key={value}><input checked={appearance === value} name="appearance" onChange={() => setAppearance(value)} type="radio" value={value} /><span className={`account-theme-sample account-theme-sample--${value}`} aria-hidden="true"><i /><i /><i /></span><strong>{value === 'light' ? 'Claro' : 'Escuro'}</strong></label>)}</fieldset><p className="account-settings-help">A preferência será salva. A aplicação dos modos claro e escuro será disponibilizada em uma próxima etapa.</p></section>
      </div>
      <footer className="account-settings-actions"><button disabled={!dirty || busy || photoBusy} onClick={() => reset(profile)} type="button">Cancelar alterações</button><button className="primary-action" disabled={!dirty || busy || photoBusy || requesting || !online} type="submit">{busy ? 'Salvando…' : 'Salvar alterações'}</button></footer>
    </form>
    <section className="account-settings-card account-security"><div><span className="account-settings-kicker">Segurança</span><h2>Redefinição de senha</h2><p>Envie um pedido ao gerente da UBS. Pedidos de contas de gerência são encaminhados à administração global.</p>{profile.passwordResetRequestedAtUtc && <p className="account-reset-pending" role="status">Pedido enviado em {new Date(profile.passwordResetRequestedAtUtc).toLocaleString('pt-BR')}. Aguardando atendimento.</p>}</div><button disabled={requesting || busy || !online || Boolean(profile.passwordResetRequestedAtUtc)} onClick={() => void sendRequest()} type="button">{requesting ? 'Enviando…' : profile.passwordResetRequestedAtUtc ? 'Pedido enviado' : 'Solicitar redefinição de senha'}</button></section>
  </section>
}

async function normalizedPhoto(file: File): Promise<string> {
  if (file.size > 5 * 1024 * 1024 || !['image/png', 'image/jpeg', 'image/webp'].includes(file.type)) throw new Error('Escolha uma foto PNG, JPG ou WebP de até 5 MB.')
  const url = await new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(new Error('Não foi possível ler esta foto.'))
    reader.readAsDataURL(file)
  })
    const image = new Image(); image.src = url; await image.decode()
    const canvas = document.createElement('canvas'); canvas.width = 256; canvas.height = 256
    const context = canvas.getContext('2d'); if (!context || !image.naturalWidth || !image.naturalHeight) throw new Error('Não foi possível preparar esta foto.')
    const side = Math.min(image.naturalWidth, image.naturalHeight)
    context.drawImage(image, (image.naturalWidth - side) / 2, (image.naturalHeight - side) / 2, side, side, 0, 0, 256, 256)
    const blob = await new Promise<Blob>((resolve, reject) => canvas.toBlob(value => value ? resolve(value) : reject(new Error('Não foi possível preparar esta foto.')), 'image/png'))
    const bytes = new Uint8Array(await blob.arrayBuffer()); let binary = ''; for (const value of bytes) binary += String.fromCharCode(value)
    return btoa(binary)
}
