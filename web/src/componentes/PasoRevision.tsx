import { Fragment, useMemo, useState } from 'react'
import { etiquetaCampo } from '../formato'
import type { Lote } from '../tipos'
import { Aviso, EstadoChip, Tarjeta } from './Comunes'
import { DetalleEnlace } from './DetalleEnlace'

type Filtro = 'todas' | 'listas' | 'errores'

export function PasoRevision({ lote, onContinuar, onNuevo }: { lote: Lote; onContinuar: () => void; onNuevo: () => void }) {
  const [filtro, setFiltro] = useState<Filtro>(lote.resumen.invalida > 0 ? 'errores' : 'todas')
  const [abierta, setAbierta] = useState<number | null>(null)
  const filas = useMemo(
    () => lote.filas.filter((f) => filtro === 'todas' || (filtro === 'listas' ? f.enlace : !f.enlace)),
    [lote, filtro],
  )
  const listas = lote.resumen.lista

  return (
    <section className="panel">
      <div className="panel-encabezado">
        <div>
          <h1>Revisar datos</h1>
          <p className="texto-suave">
            {lote.nombreArchivo}{lote.hoja && lote.hoja !== 'CSV' ? ` · hoja «${lote.hoja}»` : ''}
          </p>
        </div>
        <div className="acciones">
          <button type="button" className="btn btn-secundario" onClick={onNuevo}>Cargar otro archivo</button>
          <button type="button" className="btn btn-primario" disabled={listas === 0} onClick={onContinuar}>
            Continuar con {listas} {listas === 1 ? 'correo' : 'correos'}
          </button>
        </div>
      </div>

      <div className="tarjetas">
        <Tarjeta titulo="Filas leídas" valor={lote.resumen.total} />
        <Tarjeta titulo="Listas para enviar" valor={listas} tono="exito" />
        <Tarjeta titulo="Con errores (no se envían)" valor={lote.resumen.invalida} tono={lote.resumen.invalida ? 'error' : undefined} />
      </div>

      {lote.erroresArchivo.map((e) => <Aviso key={e} tipo="advertencia">{e}</Aviso>)}
      {lote.resumen.invalida > 0 && listas > 0 && (
        <Aviso tipo="advertencia">
          Las filas con errores no se enviarán. Puede corregirlas en el archivo y volver a cargarlo, o continuar solo con las filas listas.
        </Aviso>
      )}

      <div className="filtros" role="tablist">
        {(['todas', 'listas', 'errores'] as Filtro[]).map((f) => (
          <button key={f} type="button" role="tab" aria-selected={filtro === f} className={filtro === f ? 'activo' : ''} onClick={() => setFiltro(f)}>
            {f === 'todas' ? `Todas (${lote.resumen.total})` : f === 'listas' ? `Listas (${listas})` : `Con errores (${lote.resumen.invalida})`}
          </button>
        ))}
      </div>

      <div className="tabla-contenedor">
        <table className="tabla">
          <thead>
            <tr>
              <th>Fila</th><th>Tipo</th><th>Nombre del contratante</th><th>Nombre del cliente</th>
              <th>Agrupador</th><th>Correo</th><th>Estado</th><th>Enlace</th>
            </tr>
          </thead>
          <tbody>
            {filas.length === 0 && (
              <tr><td colSpan={8} className="vacio">No hay filas en este filtro.</td></tr>
            )}
            {filas.map((f) => {
              const conError = (campo: string) => f.errores.some((e) => e.campo === campo) ? 'celda-error' : ''
              return (
                <Fragment key={f.numeroFila}>
                  <tr className={f.enlace ? '' : 'fila-invalida'}>
                    <td className="num">{f.numeroFila}</td>
                    <td className={conError('TipoFormulario')}>{f.tipoFormulario || '—'}</td>
                    <td className={conError('NombreContratante')}>{f.nombreContratante || '—'}</td>
                    <td className={conError('NombreCliente')}>{f.nombreCliente || '—'}</td>
                    <td className={conError('NumeroPoliza')}>{f.numeroPoliza || '—'}</td>
                    <td className={conError('Correo')}>{f.correo || '—'}</td>
                    <td><EstadoChip estado={f.estado} /></td>
                    <td>
                      {f.enlace ? (
                        <button type="button" className="btn btn-mini" aria-expanded={abierta === f.numeroFila}
                          onClick={() => setAbierta(abierta === f.numeroFila ? null : f.numeroFila)}>
                          {abierta === f.numeroFila ? 'Ocultar' : 'Ver enlace'}
                        </button>
                      ) : '—'}
                    </td>
                  </tr>
                  {f.errores.length > 0 && (
                    <tr className="fila-detalle fila-invalida">
                      <td />
                      <td colSpan={7}>
                        <ul className="lista-errores">
                          {f.errores.map((e, i) => <li key={i}><strong>{etiquetaCampo[e.campo] ?? e.campo}:</strong> {e.mensaje}</li>)}
                        </ul>
                      </td>
                    </tr>
                  )}
                  {abierta === f.numeroFila && f.enlace && (
                    <tr className="fila-detalle"><td /><td colSpan={7}><DetalleEnlace enlace={f.enlace} /></td></tr>
                  )}
                </Fragment>
              )
            })}
          </tbody>
        </table>
      </div>
    </section>
  )
}
