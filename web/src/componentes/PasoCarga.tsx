import { useRef, useState } from 'react'
import { api, ErrorApi } from '../api'
import { formatoBytes } from '../formato'
import type { Configuracion, Lote } from '../tipos'
import { Aviso } from './Comunes'

export function PasoCarga({ config, onLote }: { config: Configuracion; onLote: (l: Lote) => void }) {
  const input = useRef<HTMLInputElement>(null)
  const [arrastrando, setArrastrando] = useState(false)
  const [cargando, setCargando] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function procesar(archivo: File | undefined) {
    if (!archivo) return
    setError(null)
    const ext = archivo.name.slice(archivo.name.lastIndexOf('.')).toLowerCase()
    if (!config.extensiones.includes(ext)) return setError(`Formato no permitido. Use ${config.extensiones.join(', ')}.`)
    if (archivo.size > config.tamanoMaximoBytes) return setError(`El archivo supera ${formatoBytes(config.tamanoMaximoBytes)}.`)
    setCargando(archivo.name)
    try {
      onLote(await api.subir(archivo))
    } catch (e) {
      setError(e instanceof ErrorApi ? e.message : 'No se pudo procesar el archivo.')
    } finally {
      setCargando(null)
      if (input.current) input.current.value = ''
    }
  }

  return (
    <section className="panel">
      <div className="panel-encabezado">
        <div>
          <h1>Cargar destinatarios</h1>
          <p className="texto-suave">
            Suba el archivo con los clientes. Se validará cada fila y se generará su enlace cifrado con vigencia de{' '}
            {config.vigenciaHoras} horas.
          </p>
        </div>
        <a className="btn btn-secundario" href={api.urlPlantilla} download>
          Descargar plantilla
        </a>
      </div>

      <div
        className={`zona-carga ${arrastrando ? 'activa' : ''} ${cargando ? 'cargando' : ''}`}
        onDragOver={(e) => { e.preventDefault(); setArrastrando(true) }}
        onDragLeave={() => setArrastrando(false)}
        onDrop={(e) => { e.preventDefault(); setArrastrando(false); procesar(e.dataTransfer.files[0]) }}
        onClick={() => !cargando && input.current?.click()}
        onKeyDown={(e) => (e.key === 'Enter' || e.key === ' ') && input.current?.click()}
        role="button"
        tabIndex={0}
        aria-label="Seleccionar archivo"
      >
        <input ref={input} type="file" hidden accept={config.extensiones.join(',')} onChange={(e) => procesar(e.target.files?.[0])} />
        <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true">
          <path d="M12 16V4m0 0-4 4m4-4 4 4M4 16v3a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-3" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
        {cargando ? (
          <p><strong>Procesando {cargando}…</strong><br />Validando filas y cifrando enlaces.</p>
        ) : (
          <p><strong>Arrastre el archivo aquí o haga clic para seleccionarlo</strong><br />
            {config.extensiones.join(', ')} · hasta {formatoBytes(config.tamanoMaximoBytes)} · máximo {config.filasMaximas} filas</p>
        )}
      </div>

      {error && <Aviso tipo="error">{error}</Aviso>}

      <div className="columnas-esperadas">
        <h2>Columnas esperadas</h2>
        <p className="texto-suave">En la hoja «Destinatarios» (o la primera hoja con estos encabezados). El orden no importa; la columna «Enlace» es opcional y se ignora.</p>
        <ul>
          {config.columnas.map((c) => <li key={c}>{c}</li>)}
        </ul>
        <p className="texto-suave">Tipos de formulario permitidos: {config.tiposFormulario.join(', ')}.</p>
      </div>
    </section>
  )
}
