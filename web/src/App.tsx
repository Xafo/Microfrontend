import { useCallback, useEffect, useState } from 'react'
import { api, ErrorApi, registrarSesionExpirada } from './api'
import { Aviso } from './componentes/Comunes'
import { DescargaCampana } from './componentes/DescargaCampana'
import { Encabezado } from './componentes/Encabezado'
import { Login } from './componentes/Login'
import { PasoCarga } from './componentes/PasoCarga'
import { PasoCorreo } from './componentes/PasoCorreo'
import { PasoEnvio } from './componentes/PasoEnvio'
import { PasoRevision } from './componentes/PasoRevision'
import { Pasos, type Paso } from './componentes/Pasos'
import type { Configuracion, EstadoEnvio, Lote } from './tipos'

export function App() {
  // undefined = comprobando sesión; null = sin sesión (muestra el login)
  const [usuario, setUsuario] = useState<string | null | undefined>(undefined)
  const [avisoLogin, setAvisoLogin] = useState<string | null>(null)
  const [config, setConfig] = useState<Configuracion | null>(null)
  const [errorConfig, setErrorConfig] = useState<string | null>(null)
  const [paso, setPaso] = useState<Paso>('carga')
  const [lote, setLote] = useState<Lote | null>(null)
  const [estado, setEstado] = useState<EstadoEnvio | null>(null)
  const [modo, setModo] = useState<'envio' | 'campana'>('envio')

  useEffect(() => {
    registrarSesionExpirada(() => {
      setUsuario(null)
      setAvisoLogin('Su sesión expiró. Ingrese de nuevo para continuar.')
    })
    api.sesion().then((r) => setUsuario(r.usuario)).catch(() => setUsuario(null))
  }, [])

  useEffect(() => {
    if (!usuario || config) return
    api.configuracion()
      .then((c) => setConfig(c))
      .catch((e) => setErrorConfig(e instanceof ErrorApi ? e.message : 'No se pudo cargar la configuración.'))
  }, [usuario, config])

  async function cerrarSesion() {
    if (estado?.estado === 'Enviando' && !window.confirm('Hay un envío en curso. Seguirá ejecutándose en el servidor. ¿Cerrar sesión?')) return
    try { await api.logout() } catch { /* la sesión se cierra igual en pantalla */ }
    setAvisoLogin(null)
    setUsuario(null)
    setConfig(null)
    nuevo()
  }

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

  if (usuario === undefined) return <div className="cargando-sesion" aria-busy="true"><span className="girando" /></div>
  if (usuario === null)
    return <Login aviso={avisoLogin} onIngreso={(u) => { setAvisoLogin(null); setErrorConfig(null); setUsuario(u) }} />

  return (
    <>
      <Encabezado config={config} usuario={usuario} onCerrarSesion={cerrarSesion} />
      <main className="contenido">
        {errorConfig && <Aviso tipo="error">{errorConfig}</Aviso>}
        {config && (
          <>
            <div className="modos" role="tablist" aria-label="Opciones del portal">
              <button type="button" role="tab" aria-selected={modo === 'envio'} className={modo === 'envio' ? 'activo' : ''}
                onClick={() => setModo('envio')}>Enviar enlaces</button>
              <button type="button" role="tab" aria-selected={modo === 'campana'} className={modo === 'campana' ? 'activo' : ''}
                onClick={() => setModo('campana')}>Descargar información de campaña</button>
            </div>
            {modo === 'campana' && <DescargaCampana config={config} />}
            {modo === 'envio' && <>
            <Pasos actual={paso} habilitados={habilitados} onIr={(p) => (p === 'carga' ? nuevo() : setPaso(p))} />
            {paso === 'carga' && <PasoCarga config={config} onLote={(l) => { setLote(l); setEstado(null); setPaso('revision') }} />}
            {paso === 'revision' && lote && <PasoRevision lote={lote} onNuevo={nuevo} onContinuar={() => setPaso('correo')} />}
            {paso === 'correo' && lote && (
              <PasoCorreo lote={lote} config={config}
                onAtras={() => setPaso('revision')} onEnviado={() => { setPaso('envio'); consultar() }} />
            )}
            {paso === 'envio' && lote && <PasoEnvio lote={lote} estado={estado} onActualizar={setEstado} onNuevo={nuevo} />}
            </>}
          </>
        )}
      </main>
      <footer className="pie">Ficohsa Seguros · Uso interno</footer>
    </>
  )
}
