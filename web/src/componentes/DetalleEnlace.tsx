import { formatoFecha } from '../formato'
import type { Enlace } from '../tipos'
import { BotonCopiar } from './Comunes'

export function DetalleEnlace({ enlace }: { enlace: Enlace }) {
  return (
    <div className="detalle-enlace">
      <div className="detalle-fila">
        <span className="detalle-etiqueta">Enlace cifrado (el que recibe el cliente)</span>
        <code className="enlace-texto">{enlace.cifrado}</code>
        <div className="detalle-acciones">
          <BotonCopiar texto={enlace.cifrado} />
          <span className="texto-suave">{enlace.longitud} caracteres · vence {formatoFecha(enlace.expiraEnUtc)}</span>
        </div>
      </div>
      {enlace.referencia && (
        <div className="detalle-fila">
          <span className="detalle-etiqueta">Enlace real sin cifrar (solo referencia, no se envía)</span>
          <code className="enlace-texto enlace-referencia">{enlace.referencia}</code>
          <div className="detalle-acciones"><BotonCopiar texto={enlace.referencia} /></div>
        </div>
      )}
    </div>
  )
}
