import { useState } from 'react'
import { api, ErrorApi } from '../api'
import { formatoFecha } from '../formato'
import type { EstadoEnvio, Lote } from '../tipos'
import { Aviso, EstadoChip, Tarjeta } from './Comunes'

interface Props {
  lote: Lote
  estado: EstadoEnvio | null
  onActualizar: (e: EstadoEnvio) => void
  onNuevo: () => void
}

export function PasoEnvio({ lote, estado, onActualizar, onNuevo }: Props) {
  const [error, setError] = useState<string | null>(null)
  if (!estado) return <section className="panel"><p>Cargando estado…</p></section>

  const r = estado.resumen
  const aEnviar = r.total - r.invalida
  const procesadas = r.enviada + r.fallida + r.cancelada
  const pct = aEnviar ? Math.round((procesadas / aEnviar) * 100) : 0
  const enCurso = estado.estado === 'Enviando'
  const porFila = new Map(estado.filas.map((f) => [f.numeroFila, f]))
  const filas = lote.filas.filter((f) => porFila.get(f.numeroFila)?.estado !== 'Invalida')

  async function accion(fn: () => Promise<EstadoEnvio>) {
    setError(null)
    try { onActualizar(await fn()) } catch (e) { setError(e instanceof ErrorApi ? e.message : 'La acción falló.') }
  }

  return (
    <section className="panel">
      <div className="panel-encabezado">
        <div>
          <h1>{enCurso ? 'Enviando correos…' : 'Envío finalizado'}</h1>
          <p className="texto-suave">{lote.nombreArchivo}</p>
        </div>
        <div className="acciones">
          {enCurso && (
            <button type="button" className="btn btn-secundario" disabled={estado.cancelacionSolicitada} onClick={() => accion(() => api.cancelar(lote.id))}>
              {estado.cancelacionSolicitada ? 'Cancelando…' : 'Detener envío'}
            </button>
          )}
          {!enCurso && r.fallida + r.cancelada > 0 && (
            <button type="button" className="btn btn-secundario" onClick={() => accion(() => api.reintentar(lote.id))}>
              Reintentar {r.fallida + r.cancelada} {r.fallida + r.cancelada === 1 ? 'fallido' : 'fallidos'}
            </button>
          )}
          {!enCurso && <button type="button" className="btn btn-primario" onClick={onNuevo}>Nuevo envío</button>}
        </div>
      </div>

      <div className="progreso" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={pct} aria-label="Progreso del envío">
        <div className="progreso-barra" style={{ width: `${pct}%` }} />
      </div>
      <p className="progreso-texto">{procesadas} de {aEnviar} procesados ({pct}%)</p>

      <div className="tarjetas">
        <Tarjeta titulo="Enviados" valor={r.enviada} tono="exito" />
        <Tarjeta titulo="Pendientes" valor={r.pendiente + r.enviando} />
        <Tarjeta titulo="Fallidos" valor={r.fallida} tono={r.fallida ? 'error' : undefined} />
        <Tarjeta titulo="Cancelados" valor={r.cancelada} />
      </div>

      {error && <Aviso tipo="error">{error}</Aviso>}
      {!enCurso && r.fallida === 0 && r.cancelada === 0 && <Aviso tipo="exito">pr_mail se ejecutó sin errores para todos los destinatarios.</Aviso>}

      <div className="tabla-contenedor">
        <table className="tabla">
          <thead>
            <tr><th>Fila</th><th>Nombre del cliente</th><th>Número de póliza</th><th>Correo</th><th>Estado</th><th>Hora</th><th>Detalle</th></tr>
          </thead>
          <tbody>
            {filas.map((f) => {
              const e = porFila.get(f.numeroFila)
              return (
                <tr key={f.numeroFila} className={e?.estado === 'Fallida' ? 'fila-invalida' : ''}>
                  <td className="num">{f.numeroFila}</td>
                  <td>{f.nombreCliente}</td>
                  <td>{f.numeroPoliza}</td>
                  <td>{f.correo}</td>
                  <td><EstadoChip estado={e?.estado ?? f.estado} /></td>
                  <td>{formatoFecha(e?.enviadaUtc ?? null)}</td>
                  <td className="detalle-mensaje">{e?.mensaje ?? ''}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </section>
  )
}
