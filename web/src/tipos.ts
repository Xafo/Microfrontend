export type EstadoFila = 'Invalida' | 'Lista' | 'Pendiente' | 'Enviando' | 'Enviada' | 'Fallida' | 'Cancelada'
export type EstadoLote = 'Preparado' | 'Enviando' | 'Finalizado'

export interface Configuracion {
  ambiente: string
  urlPublicaBase: string
  vigenciaHoras: number
  tiposFormulario: string[]
  columnas: string[]
  extensiones: string[]
  tamanoMaximoBytes: number
  filasMaximas: number
  modoEnvio: 'Simulado' | 'Oracle'
  remitente: string
  asuntoPorDefecto: string
  cuerpoPorDefecto: string
  marcadores: string[]
}

export interface Enlace {
  cifrado: string
  referencia: string | null
  expiraEnUtc: string
  longitud: number
  version: number
}

export interface Fila {
  numeroFila: number
  tipoFormulario: string
  nombreContratante: string
  nombreCliente: string
  numeroPoliza: string
  sumaAsegurada: string | null
  sumaAseguradaTexto: string
  correo: string
  errores: { campo: string; mensaje: string }[]
  enlace: Enlace | null
  estado: EstadoFila
  mensaje: string | null
  enviadaUtc: string | null
}

export type Resumen = Record<'total' | 'invalida' | 'lista' | 'pendiente' | 'enviando' | 'enviada' | 'fallida' | 'cancelada', number>

export interface Lote {
  id: string
  nombreArchivo: string
  hoja: string | null
  creadoUtc: string
  estado: EstadoLote
  erroresArchivo: string[]
  resumen: Resumen
  filas: Fila[]
}

export interface EstadoEnvio {
  id: string
  estado: EstadoLote
  cancelacionSolicitada: boolean
  resumen: Resumen
  filas: { numeroFila: number; estado: EstadoFila; mensaje: string | null; enviadaUtc: string | null }[]
}

export interface VistaPrevia {
  asunto: string
  asuntoModificado: boolean
  html: string
  errores: string[]
}
