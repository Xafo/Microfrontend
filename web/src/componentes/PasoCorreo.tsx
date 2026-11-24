import { useEffect, useState } from 'react'
import { api, ErrorApi } from '../api'
import type { Configuracion, Lote, VistaPrevia } from '../tipos'
import { Aviso, Confirmacion } from './Comunes'

interface Props {
  lote: Lote
  config: Configuracion
  onAtras: () => void
  onEnviado: () => void
}

/** El correo es fijo (diseño institucional): aquí solo se revisa cómo le llegará a cada cliente y se envía. */
export function PasoCorreo({ lote, config, onAtras, onEnviado }: Props) {
  const listas = lote.filas.filter((f) => f.enlace)
  const [filaPrevia, setFilaPrevia] = useState(listas[0]?.numeroFila)
  const [previa, setPrevia] = useState<VistaPrevia | null>(null)
  const [errorPrevia, setErrorPrevia] = useState<string | null>(null)
  const [confirmar, setConfirmar] = useState(false)
  const [enviando, setEnviando] = useState(false)
  const [errorEnvio, setErrorEnvio] = useState<{ mensaje: string; errores: string[] } | null>(null)

  useEffect(() => {
    let vigente = true
    setErrorPrevia(null)
    api.vistaPrevia(lote.id, filaPrevia)
      .then((r) => vigente && setPrevia(r))
      .catch((e) => vigente && setErrorPrevia(e instanceof ErrorApi ? e.message : 'No se pudo generar la vista previa.'))
    return () => { vigente = false }
  }, [lote.id, filaPrevia])

  async function enviar() {
    setConfirmar(false)
    setEnviando(true)
    setErrorEnvio(null)
    try {
      await api.enviar(lote.id)
      onEnviado()
    } catch (e) {
      setErrorEnvio(e instanceof ErrorApi ? { mensaje: e.message, errores: e.errores } : { mensaje: 'No se pudo iniciar el envío.', errores: [] })
    } finally {
      setEnviando(false)
    }
  }

  const bloqueos = [...(previa?.errores ?? []), ...(previa?.advertencias ?? [])]
  const puedeEnviar = !enviando && !errorPrevia && previa !== null && bloqueos.length === 0 && listas.length > 0

  return (
    <section className="panel">
      <div className="panel-encabezado">
        <div>
          <h1>Revisar correo</h1>
          <p className="texto-suave">
            Remitente: <strong>{config.remitente || '—'}</strong> · Se enviará a {listas.length} {listas.length === 1 ? 'destinatario' : 'destinatarios'} mediante pr_mail.
          </p>
        </div>
        <div className="acciones">
          <button type="button" className="btn btn-secundario" onClick={onAtras}>Volver a revisar</button>
          <button type="button" className="btn btn-primario" disabled={!puedeEnviar} onClick={() => setConfirmar(true)}>
            {enviando ? 'Iniciando…' : `Enviar ${listas.length} ${listas.length === 1 ? 'correo' : 'correos'}`}
          </button>
        </div>
      </div>

      {errorEnvio && (
        <Aviso tipo="error">
          {errorEnvio.mensaje}
          {errorEnvio.errores.length > 0 && <ul>{errorEnvio.errores.map((e) => <li key={e}>{e}</li>)}</ul>}
        </Aviso>
      )}
      {bloqueos.length > 0 && <Aviso tipo="advertencia"><ul>{bloqueos.map((e) => <li key={e}>{e}</li>)}</ul></Aviso>}

      <div className="correo-fijo">
        <div className="correo-datos">
          <div className="campo">
            <span>Asunto</span>
            <div className="valor-fijo">{previa?.asunto ?? config.asunto}</div>
          </div>
          <div className="campo">
            <span>Ver cómo le llega a</span>
            <select value={filaPrevia} onChange={(e) => setFilaPrevia(Number(e.target.value))} aria-label="Fila de la vista previa">
              {listas.map((f) => <option key={f.numeroFila} value={f.numeroFila}>Fila {f.numeroFila} · {f.nombreCliente}</option>)}
            </select>
          </div>
          <Aviso tipo="info">
            El correo usa el diseño institucional y no se edita. Se completan solos el <strong>nombre del cliente</strong>,
            el <strong>nombre del contratante</strong> y el <strong>enlace cifrado</strong> de cada fila.
          </Aviso>
        </div>
        <div className="previa">
          <div className="previa-asunto"><span>Asunto:</span> {previa?.asunto ?? '…'}</div>
          {errorPrevia ? <Aviso tipo="error">{errorPrevia}</Aviso>
            : <iframe title="Vista previa del correo" sandbox="" srcDoc={previa?.html ?? ''} />}
        </div>
      </div>

      {confirmar && (
        <Confirmacion titulo="Confirmar envío" confirmar={`Enviar ${listas.length} correos`} onConfirmar={enviar} onCancelar={() => setConfirmar(false)}>
          <p>Se enviarán <strong>{listas.length}</strong> correos desde <strong>{config.remitente}</strong> en el ambiente <strong>{config.ambiente}</strong>.</p>
          <p>Asunto: <em>{previa?.asunto ?? config.asunto}</em></p>
          {config.modoEnvio === 'Simulado' && <p><strong>Modo simulado:</strong> no se enviará ningún correo real.</p>}
          <p className="texto-suave">Esta acción no se puede deshacer.</p>
        </Confirmacion>
      )}
    </section>
  )
}
