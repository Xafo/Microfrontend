import { useCallback, useEffect, useState } from 'react'
import { api, ErrorApi } from './api'
import { Aviso } from './componentes/Comunes'
import { Encabezado } from './componentes/Encabezado'
import { PasoCarga } from './componentes/PasoCarga'
import { PasoCorreo } from './componentes/PasoCorreo'
import { PasoEnvio } from './componentes/PasoEnvio'
import { PasoRevision } from './componentes/PasoRevision'
import { Pasos, type Paso } from './componentes/Pasos'
import type { Configuracion, EstadoEnvio, Lote } from './tipos'

export function App() {
  const [config, setConfig] = useState<Configuracion | null>(null)
  const [errorConfig, setErrorConfig] = useState<string | null>(null)
  const [paso, setPaso] = useState<Paso>('carga')
  const [lote, setLote] = useState<Lote | null>(null)
  const [estado, setEstado] = useState<EstadoEnvio | null>(null)
  const [asunto, setAsunto] = useState('')
  const [cuerpo, setCuerpo] = useState('')

  useEffect(() => {
    api.configuracion()
      .then((c) => { setConfig(c); setAsunto(c.asuntoPorDefecto); setCuerpo(c.cuerpoPorDefecto) })
      .catch((e) => setErrorConfig(e instanceof ErrorApi ? e.message : 'No se pudo cargar la configuración.'))
  }, [])

  const consultar = useCallback(async () => {
    if (!lote) return
    try { setEstado(await api.estado(lote.id)) } catch { /* se reintenta en el siguiente ciclo */ }
  }, [lote])

  // Consulta el avance mientras el lote se envía.
  useEffect(() => {
    if (paso !== 'envio' || estado?.estado === 'Finalizado') return
    const t = setInterval(consultar, 1000)
    return () => clearInterval(t)
  }, [paso, estado?.estado, consultar])

  const enviando = estado?.estado === 'Enviando'
  useEffect(() => {
    if (!enviando) return
    const aviso = (e: BeforeUnloadEvent) => e.preventDefault()
    window.addEventListener('beforeunload', aviso)
    return () => window.removeEventListener('beforeunload', aviso)
  }, [enviando])

  function nuevo() {
    setLote(null)
    setEstado(null)
    setPaso('carga')
  }

  const habilitados: Paso[] = estado
    ? ['envio']
    : lote ? ['carga', 'revision', ...(lote.resumen.lista > 0 ? (['correo'] as Paso[]) : [])] : ['carga']

  return (
    <>
      <Encabezado config={config} />
      <main className="contenido">
        {errorConfig && <Aviso tipo="error">{errorConfig}</Aviso>}
        {config && (
          <>
            <Pasos actual={paso} habilitados={habilitados} onIr={(p) => (p === 'carga' ? nuevo() : setPaso(p))} />
            {paso === 'carga' && <PasoCarga config={config} onLote={(l) => { setLote(l); setEstado(null); setPaso('revision') }} />}
            {paso === 'revision' && lote && <PasoRevision lote={lote} onNuevo={nuevo} onContinuar={() => setPaso('correo')} />}
            {paso === 'correo' && lote && (
              <PasoCorreo lote={lote} config={config} asunto={asunto} cuerpo={cuerpo} onAsunto={setAsunto} onCuerpo={setCuerpo}
                onAtras={() => setPaso('revision')} onEnviado={() => { setPaso('envio'); consultar() }} />
            )}
            {paso === 'envio' && lote && <PasoEnvio lote={lote} estado={estado} onActualizar={setEstado} onNuevo={nuevo} />}
          </>
        )}
      </main>
      <footer className="pie">Ficohsa Seguros · Uso interno</footer>
    </>
  )
}
