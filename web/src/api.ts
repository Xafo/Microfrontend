import type { Configuracion, EstadoEnvio, Lote, VistaPrevia } from './tipos'

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
  vistaPrevia: (id: string, asunto: string, cuerpo: string, numeroFila?: number) =>
    pedir<VistaPrevia>(`api/lotes/${id}/vista-previa`, json({ asunto, cuerpo, numeroFila })),
  enviar: (id: string, asunto: string, cuerpo: string) => pedir<EstadoEnvio>(`api/lotes/${id}/envio`, json({ asunto, cuerpo })),
  reintentar: (id: string) => pedir<EstadoEnvio>(`api/lotes/${id}/reintentar`, { method: 'POST' }),
  cancelar: (id: string) => pedir<EstadoEnvio>(`api/lotes/${id}/cancelar`, { method: 'POST' }),
}
