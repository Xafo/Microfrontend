import type { Configuracion, DescargaCampana, EstadoEnvio, Lote, VistaPrevia } from './tipos'

export class ErrorApi extends Error {
  constructor(message: string, public estado: number, public errores: string[] = []) {
    super(message)
  }
}

let alExpirarSesion: () => void = () => {}
/** La app registra aquí qué hacer si el servidor responde 401 (sesión vencida o cerrada). */
export function registrarSesionExpirada(fn: () => void) { alExpirarSesion = fn }

// Rutas relativas: la app vive en el mismo sitio IIS que la API.
async function pedir<T>(ruta: string, init?: RequestInit): Promise<T> {
  let r: Response
  try {
    r = await fetch(ruta, {
      ...init,
      cache: 'no-store',
      credentials: 'same-origin',
      // Cabecera propia del portal: el servidor rechaza los POST que no la traen (protección CSRF).
      headers: { ...(init?.headers ?? {}), 'X-Solicitud': '1' },
    })
  } catch {
    throw new ErrorApi('No se pudo conectar con el servidor.', 0)
  }
  if (r.status === 401 && !ruta.startsWith('api/auth/')) alExpirarSesion()
  if (!r.ok) {
    let detalle = `Error ${r.status}`
    let errores: string[] = []
    try {
      const p = await r.json()
      detalle = p.detail ?? p.title ?? detalle
      errores = p.errores ?? []
    } catch { /* respuesta sin cuerpo JSON */ }
    throw new ErrorApi(detalle, r.status, errores)
  }
  if (r.status === 204) return undefined as T
  // El portal siempre responde JSON en /api. Si llega HTML, la respuesta la generó otro equipo
  // en el camino (WAF, proxy o balanceador), no la aplicación.
  if (!(r.headers.get('content-type') ?? '').includes('json'))
    throw new ErrorApi(
      'La petición fue bloqueada antes de llegar al portal (posible firewall/WAF). Consulte con Seguridad o Infraestructura.',
      r.status,
    )
  return r.json() as Promise<T>
}

/** Excel de carga masiva de una campaña (solicitudes ENVIADAS del agrupador). */
async function descargarCampana(agrupador: string): Promise<DescargaCampana> {
  let r: Response
  try {
    r = await fetch(`api/campanas/excel?agrupador=${encodeURIComponent(agrupador)}`, {
      cache: 'no-store', credentials: 'same-origin', headers: { 'X-Solicitud': '1' },
    })
  } catch {
    throw new ErrorApi('No se pudo conectar con el servidor.', 0)
  }
  if (r.status === 401) alExpirarSesion()
  const tipo = r.headers.get('content-type') ?? ''
  if (!r.ok) {
    let detalle = `Error ${r.status}`
    if (tipo.includes('json')) {
      try { const p = await r.json(); detalle = p.detail ?? p.title ?? detalle } catch { /* sin cuerpo */ }
    } else {
      detalle = 'La petición fue bloqueada antes de llegar al portal (posible firewall/WAF). Consulte con Seguridad o Infraestructura.'
    }
    throw new ErrorApi(detalle, r.status)
  }
  if (!tipo.includes('spreadsheetml'))
    throw new ErrorApi('La respuesta no es un archivo de Excel (posible bloqueo de firewall/WAF).', r.status)
  const nombre = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(r.headers.get('content-disposition') ?? '')?.[1]
  return {
    archivo: await r.blob(),
    nombre: nombre ? decodeURIComponent(nombre) : `CARGA_MASIVA_${agrupador}.xlsx`,
    total: Number(r.headers.get('x-total-solicitudes') ?? 0),
  }
}

const json = (body: unknown): RequestInit => ({
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
})

export const api = {
  sesion: () => pedir<{ usuario: string }>('api/auth/sesion'),
  login: (usuario: string, clave: string) => pedir<{ usuario: string }>('api/auth/login', json({ usuario, clave })),
  logout: () => pedir<void>('api/auth/logout', { method: 'POST' }),
  configuracion: () => pedir<Configuracion>('api/configuracion'),
  urlPlantilla: 'api/plantilla',
  subir: (archivo: File) => {
    const fd = new FormData()
    fd.append('archivo', archivo)
    return pedir<Lote>('api/lotes', { method: 'POST', body: fd })
  },
  estado: (id: string) => pedir<EstadoEnvio>(`api/lotes/${id}/estado`),
  // El correo es fijo: solo se indica la fila. Ya no viaja HTML (lo que bloqueaba el WAF).
  vistaPrevia: (id: string, numeroFila?: number) =>
    pedir<VistaPrevia>(`api/lotes/${id}/vista-previa${numeroFila ? `?numeroFila=${numeroFila}` : ''}`),
  enviar: (id: string) => pedir<EstadoEnvio>(`api/lotes/${id}/envio`, { method: 'POST' }),
  descargarCampana,
  reintentar: (id: string) => pedir<EstadoEnvio>(`api/lotes/${id}/reintentar`, { method: 'POST' }),
  cancelar: (id: string) => pedir<EstadoEnvio>(`api/lotes/${id}/cancelar`, { method: 'POST' }),
}
