import { useState, type FormEvent, type KeyboardEvent } from 'react'
import { api, ErrorApi } from '../api'
import { Logo } from './Logo'

export function Login({ aviso, onIngreso }: { aviso?: string | null; onIngreso: (usuario: string) => void }) {
  const [usuario, setUsuario] = useState('')
  const [clave, setClave] = useState('')
  const [verClave, setVerClave] = useState(false)
  const [mayus, setMayus] = useState(false)
  const [enviando, setEnviando] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function ingresar(e: FormEvent) {
    e.preventDefault()
    if (!usuario.trim() || !clave) return setError('Ingrese su usuario y contraseña.')
    setEnviando(true)
    setError(null)
    try {
      const r = await api.login(usuario.trim(), clave)
      onIngreso(r.usuario)
    } catch (err) {
      setClave('')
      setError(err instanceof ErrorApi
        ? err.estado === 429 ? 'Demasiados intentos. Espere un minuto y vuelva a intentarlo.' : err.message
        : 'No se pudo iniciar sesión.')
    } finally {
      setEnviando(false)
    }
  }

  const detectarMayus = (e: KeyboardEvent<HTMLInputElement>) => setMayus(e.getModifierState('CapsLock'))

  return (
    <div className="login">
      <section className="login-marca" aria-hidden="true">
        <div className="login-marca-contenido">
          <Logo className="login-logo" />
          <h1>Envío de enlaces de enrolamiento</h1>
          <p>Cargue el listado de clientes, revise los datos y envíe a cada uno su enlace seguro al formulario de enrolamiento.</p>
          <ul className="login-puntos">
            <li>Enlaces cifrados con vigencia de 48 horas</li>
            <li>Validación de cada fila antes de enviar</li>
            <li>Seguimiento del envío en tiempo real</li>
          </ul>
        </div>
        <div className="login-ondas" />
      </section>

      <section className="login-panel">
        <form className="login-tarjeta" onSubmit={ingresar} noValidate>
          <div className="login-logo-movil"><Logo className="login-logo" /></div>
          <h2>Iniciar sesión</h2>
          <p className="texto-suave">Ingrese con las credenciales asignadas al portal.</p>

          {aviso && !error && <div className="aviso aviso-info" role="status">{aviso}</div>}
          {error && <div className="aviso aviso-error" role="alert">{error}</div>}

          <label className="campo">
            <span>Usuario</span>
            <div className="campo-icono">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
                <circle cx="12" cy="8" r="4" /><path d="M4 20c0-4 4-6 8-6s8 2 8 6" strokeLinecap="round" />
              </svg>
              <input type="text" name="username" autoComplete="username" autoFocus value={usuario}
                onChange={(e) => setUsuario(e.target.value)} disabled={enviando} />
            </div>
          </label>

          <label className="campo">
            <span>Contraseña</span>
            <div className="campo-icono">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true">
                <rect x="5" y="11" width="14" height="9" rx="2" /><path d="M8 11V8a4 4 0 0 1 8 0v3" strokeLinecap="round" />
              </svg>
              <input type={verClave ? 'text' : 'password'} name="password" autoComplete="current-password" value={clave}
                onChange={(e) => setClave(e.target.value)} onKeyUp={detectarMayus} onKeyDown={detectarMayus} disabled={enviando} />
              <button type="button" className="ver-clave" onClick={() => setVerClave(!verClave)}
                aria-label={verClave ? 'Ocultar contraseña' : 'Mostrar contraseña'} aria-pressed={verClave}>
                {verClave ? 'Ocultar' : 'Mostrar'}
              </button>
            </div>
            {mayus && <small className="mayus">Bloq Mayús está activado</small>}
          </label>

          <button type="submit" className="btn btn-primario btn-bloque" disabled={enviando}>
            {enviando ? <><span className="girando" aria-hidden="true" /> Ingresando…</> : 'Ingresar'}
          </button>
          <p className="login-pie">Ficohsa Seguros · Uso interno</p>
        </form>
      </section>
    </div>
  )
}
