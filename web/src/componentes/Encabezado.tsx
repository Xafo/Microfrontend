import type { Configuracion } from '../tipos'
import { Logo } from './Logo'

const nombreAmbiente: Record<string, string> = { Local: 'LOCAL', Development: 'DEV', Staging: 'STG', Production: 'PROD' }

interface Props {
  config: Configuracion | null
  usuario?: string | null
  onCerrarSesion?: () => void
}

export function Encabezado({ config, usuario, onCerrarSesion }: Props) {
  const amb = config ? nombreAmbiente[config.ambiente] ?? config.ambiente : null
  return (
    <header className="barra">
      <div className="barra-contenido">
        <div className="marca">
          <Logo className="marca-logo" />
        </div>
        <div className="barra-titulo">Envío de enlaces de enrolamiento</div>
        <div className="barra-meta">
          {amb && <span className={`ambiente ambiente-${amb.toLowerCase()}`}>{amb}</span>}
          {config?.modoEnvio === 'Simulado' && <span className="ambiente ambiente-simulado">Envío simulado</span>}
          {usuario && (
            <div className="sesion">
              <span className="sesion-usuario" title="Usuario conectado">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
                  <circle cx="12" cy="8" r="4" /><path d="M4 20c0-4 4-6 8-6s8 2 8 6" strokeLinecap="round" />
                </svg>
                {usuario}
              </span>
              <button type="button" className="btn-salir" onClick={onCerrarSesion}>Cerrar sesión</button>
            </div>
          )}
        </div>
      </div>
    </header>
  )
}
