import { useState, type FormEvent } from 'react'
import { api, ErrorApi } from '../api'
import type { Configuracion } from '../tipos'
import { Aviso } from './Comunes'

/** Descarga el Excel de carga masiva con las solicitudes ENVIADAS de una campaña (agrupador). */
export function DescargaCampana({ config }: { config: Configuracion }) {
  const [agrupador, setAgrupador] = useState('')
  const [descargando, setDescargando] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [resultado, setResultado] = useState<{ total: number; nombre: string; agrupador: string } | null>(null)

  async function descargar(e: FormEvent) {
    e.preventDefault()
    const id = agrupador.trim()
    if (!id) return setError('Ingrese el ID de la campaña.')
    setDescargando(true)
    setError(null)
    setResultado(null)
    try {
      const r = await api.descargarCampana(id)
      const url = URL.createObjectURL(r.archivo)
      const a = document.createElement('a')
      a.href = url
      a.download = r.nombre
      document.body.appendChild(a)
      a.click()
      a.remove()
      setTimeout(() => URL.revokeObjectURL(url), 10_000)
      setResultado({ total: r.total, nombre: r.nombre, agrupador: id })
    } catch (err) {
      setError(err instanceof ErrorApi ? err.message : 'No se pudo descargar la información de la campaña.')
    } finally {
      setDescargando(false)
    }
  }

  return (
    <section className="panel">
      <div className="panel-encabezado">
        <div>
          <h1>Descargar información de campaña</h1>
          <p className="texto-suave">
            Genera el Excel de carga masiva con las solicitudes que los clientes ya <strong>enviaron</strong> desde el formulario de enrolamiento.
          </p>
        </div>
      </div>

      {!config.descargaCampanaConfigurada && (
        <Aviso tipo="advertencia">
          La descarga no está configurada en este ambiente (API de solicitudes). Pida al administrador que configure
          <code> SolicitudesApi__Usuario</code> y <code>SolicitudesApi__Clave</code>.
        </Aviso>
      )}

      <form className="descarga" onSubmit={descargar} noValidate>
        <label className="campo">
          <span>ID de la campaña</span>
          <input type="text" value={agrupador} maxLength={60} autoComplete="off" disabled={descargando}
            placeholder="Ej.: número de póliza, número de gestión o agrupador"
            onChange={(e) => setAgrupador(e.target.value)} />
          <small className="texto-suave">Es el valor de la columna «Agrupador» del Excel con el que se enviaron los correos.</small>
        </label>
        <button type="submit" className="btn btn-primario" disabled={descargando || !agrupador.trim()}>
          {descargando ? <><span className="girando" aria-hidden="true" /> Generando…</> : 'Descargar Excel'}
        </button>
      </form>

      {error && <Aviso tipo="error">{error}</Aviso>}
      {resultado && (
        <Aviso tipo="exito">
          Se descargó <strong>{resultado.nombre}</strong> con <strong>{resultado.total}</strong>{' '}
          {resultado.total === 1 ? 'solicitud enviada' : 'solicitudes enviadas'} de la campaña «{resultado.agrupador}».
        </Aviso>
      )}

      <div className="columnas-esperadas">
        <h2>Qué contiene el archivo</h2>
        <p className="texto-suave">
          Una fila por cada solicitud en estado <strong>ENVIADA</strong> cuyo agrupador coincide. Se llenan las columnas de la plantilla de carga masiva:
        </p>
        <ul>
          {['Primer apellido', 'Segundo apellido', 'Primer nombre', 'Segundo nombre', 'Tipo doc.', 'Núm. identidad', 'País de nacimiento',
            'Fecha de nacimiento', 'Sexo', 'Profesión', 'Teléfono 1', 'Teléfono 2', 'Fecha de inclusión'].map((c) => <li key={c}>{c}</li>)}
        </ul>
      </div>
    </section>
  )
}
