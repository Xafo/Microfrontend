import { useState, type ReactNode } from 'react'
import { etiquetaEstado } from '../formato'

export function EstadoChip({ estado }: { estado: string }) {
  return <span className={`chip chip-${estado.toLowerCase()}`}>{etiquetaEstado[estado] ?? estado}</span>
}

export function Aviso({ tipo, children }: { tipo: 'error' | 'advertencia' | 'info' | 'exito'; children: ReactNode }) {
  return (
    <div className={`aviso aviso-${tipo}`} role={tipo === 'error' ? 'alert' : 'status'}>
      {children}
    </div>
  )
}

/** El portabapeles moderno solo funciona en HTTPS o localhost; en IIS interno por HTTP se usa el método clásico. */
async function copiar(texto: string) {
  if (navigator.clipboard && window.isSecureContext) return navigator.clipboard.writeText(texto)
  const ta = document.createElement('textarea')
  ta.value = texto
  ta.style.position = 'fixed'
  ta.style.opacity = '0'
  document.body.appendChild(ta)
  ta.select()
  document.execCommand('copy')
  ta.remove()
}

export function BotonCopiar({ texto, etiqueta = 'Copiar' }: { texto: string; etiqueta?: string }) {
  const [hecho, setHecho] = useState(false)
  return (
    <button
      type="button"
      className="btn btn-mini"
      onClick={async () => {
        await copiar(texto)
        setHecho(true)
        setTimeout(() => setHecho(false), 1500)
      }}
    >
      {hecho ? 'Copiado' : etiqueta}
    </button>
  )
}

export function Tarjeta({ titulo, valor, tono }: { titulo: string; valor: number; tono?: string }) {
  return (
    <div className={`tarjeta ${tono ? `tarjeta-${tono}` : ''}`}>
      <span className="tarjeta-valor">{valor}</span>
      <span className="tarjeta-titulo">{titulo}</span>
    </div>
  )
}

export function Confirmacion(props: {
  titulo: string
  children: ReactNode
  confirmar: string
  onConfirmar: () => void
  onCancelar: () => void
}) {
  return (
    <div className="modal-fondo" role="presentation" onClick={props.onCancelar}>
      <div className="modal" role="dialog" aria-modal="true" aria-labelledby="modal-titulo" onClick={(e) => e.stopPropagation()}>
        <h2 id="modal-titulo">{props.titulo}</h2>
        <div className="modal-cuerpo">{props.children}</div>
        <div className="acciones">
          <button type="button" className="btn btn-secundario" onClick={props.onCancelar}>
            Cancelar
          </button>
          <button type="button" className="btn btn-primario" onClick={props.onConfirmar} autoFocus>
            {props.confirmar}
          </button>
        </div>
      </div>
    </div>
  )
}
