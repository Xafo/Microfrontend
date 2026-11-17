import { useState } from 'react'
import type { Configuracion } from '../tipos'

const nombreAmbiente: Record<string, string> = { Local: 'LOCAL', Development: 'DEV', Staging: 'STG', Production: 'PROD' }

export function Encabezado({ config }: { config: Configuracion | null }) {
  const [sinLogo, setSinLogo] = useState(false)
  const amb = config ? nombreAmbiente[config.ambiente] ?? config.ambiente : null
  return (
    <header className="barra">
      <div className="barra-contenido">
        <div className="marca">
          {/* Coloque el logo oficial en web/public/logo-ficohsa.svg; mientras no exista se muestra el nombre. */}
          {!sinLogo ? (
            <img src="./logo-ficohsa.svg" alt="Ficohsa" className="marca-logo" onError={() => setSinLogo(true)} />
          ) : (
            <span className="marca-texto">FICOHSA</span>
          )}
          <span className="marca-separador" aria-hidden="true" />
          <span className="marca-producto">Seguros</span>
        </div>
        <div className="barra-titulo">Envío de enlaces de enrolamiento</div>
        {amb && (
          <div className="barra-meta">
            <span className={`ambiente ambiente-${amb.toLowerCase()}`}>{amb}</span>
            {config?.modoEnvio === 'Simulado' && <span className="ambiente ambiente-simulado">Envío simulado</span>}
          </div>
        )}
      </div>
    </header>
  )
}
