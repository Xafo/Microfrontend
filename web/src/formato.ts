const fecha = new Intl.DateTimeFormat('es-HN', { dateStyle: 'short', timeStyle: 'short', timeZone: 'America/Tegucigalpa' })

export const formatoFecha = (iso: string | null) => (iso ? fecha.format(new Date(iso)) : '—')
export const formatoBytes = (b: number) => (b >= 1048576 ? `${(b / 1048576).toFixed(0)} MB` : `${(b / 1024).toFixed(0)} KB`)

export const etiquetaEstado: Record<string, string> = {
  Invalida: 'Con errores',
  Lista: 'Lista',
  Pendiente: 'Pendiente',
  Enviando: 'Enviando…',
  Enviada: 'Enviada',
  Fallida: 'Falló',
  Cancelada: 'Cancelada',
}

export const etiquetaCampo: Record<string, string> = {
  TipoFormulario: 'Tipo de formulario',
  NombreContratante: 'Nombre del contratante',
  NombreCliente: 'Nombre del cliente',
  NumeroPoliza: 'Número de póliza',
  Correo: 'Correo',
  Enlace: 'Enlace',
  Fila: 'Fila',
}
