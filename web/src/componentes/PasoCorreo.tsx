import { useEffect, useRef, useState } from 'react'
import { api, ErrorApi } from '../api'
import type { Configuracion, Lote, VistaPrevia } from '../tipos'
import { Aviso, Confirmacion } from './Comunes'

interface Props {
  lote: Lote
  config: Configuracion
  asunto: string
  cuerpo: string
  onAsunto: (v: string) => void
  onCuerpo: (v: string) => void
  onAtras: () => void
  onEnviado: () => void
}

export function PasoCorreo({ lote, config, asunto, cuerpo, onAsunto, onCuerpo, onAtras, onEnviado }: Props) {
  const listas = lote.filas.filter((f) => f.enlace)
  const [filaPrevia, setFilaPrevia] = useState(listas[0]?.numeroFila)
  const [previa, setPrevia] = useState<VistaPrevia | null>(null)
  const [errorPrevia, setErrorPrevia] = useState<string | null>(null)
  const [confirmar, setConfirmar] = useState(false)
  const [enviando, setEnviando] = useState(false)
  const [errorEnvio, setErrorEnvio] = useState<{ mensaje: string; errores: string[] } | null>(null)
  const editor = useRef<HTMLTextAreaElement>(null)

  useEffect(() => {
    let vigente = true
    const t = setTimeout(async () => {
      try {
        const r = await api.vistaPrevia(lote.id, asunto, cuerpo, filaPrevia)
        if (vigente) { setPrevia(r); setErrorPrevia(null) }
      } catch (e) {
        if (vigente) setErrorPrevia(e instanceof ErrorApi ? e.message : 'No se pudo generar la vista previa.')
      }
    }, 400)
    return () => { vigente = false; clearTimeout(t) }
  }, [lote.id, asunto, cuerpo, filaPrevia])

  function insertar(marcador: string) {
    const ta = editor.current
    const texto = `{{${marcador}}}`
    if (!ta) return onCuerpo(cuerpo + texto)
    const { selectionStart: a, selectionEnd: b } = ta
    onCuerpo(cuerpo.slice(0, a) + texto + cuerpo.slice(b))
    requestAnimationFrame(() => { ta.focus(); ta.setSelectionRange(a + texto.length, a + texto.length) })
  }

  async function enviar() {
    setConfirmar(false)
    setEnviando(true)
    setErrorEnvio(null)
    try {
      await api.enviar(lote.id, asunto, cuerpo)
      onEnviado()
    } catch (e) {
      setErrorEnvio(e instanceof ErrorApi ? { mensaje: e.message, errores: e.errores } : { mensaje: 'No se pudo iniciar el envío.', errores: [] })
    } finally {
      setEnviando(false)
    }
  }

  const erroresPlantilla = previa?.errores ?? []
  const puedeEnviar = !enviando && erroresPlantilla.length === 0 && !errorPrevia && listas.length > 0

  return (
    <section className="panel">
      <div className="panel-encabezado">
        <div>
          <h1>Preparar correo</h1>
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

      <div className="editor-correo">
        <div className="editor-columna">
          <label className="campo">
            <span>Asunto</span>
            <input type="text" value={asunto} maxLength={200} onChange={(e) => onAsunto(e.target.value)} />
          </label>
          {previa?.asuntoModificado && (
            <Aviso tipo="info">pr_mail no admite tildes en el asunto; se enviará como: <strong>{previa.asunto}</strong></Aviso>
          )}

          <div className="campo">
            <div className="campo-cabecera">
              <span id="cuerpo-etiqueta">Cuerpo (HTML)</span>
              <button type="button" className="btn btn-mini" onClick={() => { onAsunto(config.asuntoPorDefecto); onCuerpo(config.cuerpoPorDefecto) }}>
                Restaurar texto por defecto
              </button>
            </div>
            <div className="marcadores" aria-label="Insertar marcador">
              {config.marcadores.map((m) => (
                <button key={m} type="button" className="marcador" onClick={() => insertar(m)}>{`{{${m}}}`}</button>
              ))}
            </div>
            <textarea ref={editor} aria-labelledby="cuerpo-etiqueta" value={cuerpo} spellCheck={false} onChange={(e) => onCuerpo(e.target.value)} rows={16} />
            <p className="texto-suave">
              El encabezado y el pie con imagen se agregan automáticamente. {'{{Enlace}}'} es obligatorio y se reemplaza por el enlace cifrado de cada cliente.
            </p>
          </div>
          {erroresPlantilla.length > 0 && (
            <Aviso tipo="error"><ul>{erroresPlantilla.map((e) => <li key={e}>{e}</li>)}</ul></Aviso>
          )}
        </div>

        <div className="editor-columna">
          <div className="campo-cabecera">
            <span>Vista previa</span>
            <select value={filaPrevia} onChange={(e) => setFilaPrevia(Number(e.target.value))} aria-label="Fila de la vista previa">
              {listas.map((f) => <option key={f.numeroFila} value={f.numeroFila}>Fila {f.numeroFila} · {f.nombreCliente}</option>)}
            </select>
          </div>
          <div className="previa">
            <div className="previa-asunto"><span>Asunto:</span> {previa?.asunto ?? '…'}</div>
            {errorPrevia ? <Aviso tipo="error">{errorPrevia}</Aviso>
              : <iframe title="Vista previa del correo" sandbox="" srcDoc={previa?.html ?? ''} />}
          </div>
        </div>
      </div>

      {confirmar && (
        <Confirmacion titulo="Confirmar envío" confirmar={`Enviar ${listas.length} correos`} onConfirmar={enviar} onCancelar={() => setConfirmar(false)}>
          <p>Se enviarán <strong>{listas.length}</strong> correos desde <strong>{config.remitente}</strong> en el ambiente <strong>{config.ambiente}</strong>.</p>
          <p>Asunto: <em>{previa?.asunto ?? asunto}</em></p>
          {config.modoEnvio === 'Simulado' && <p><strong>Modo simulado:</strong> no se enviará ningún correo real.</p>}
          <p className="texto-suave">Esta acción no se puede deshacer.</p>
        </Confirmacion>
      )}
    </section>
  )
}
