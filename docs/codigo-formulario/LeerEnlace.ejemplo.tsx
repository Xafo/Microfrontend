// Ejemplo: React, al abrir https://host/?datos=...
import { useEffect, useState } from 'react'

export interface ContextoEnlace {
  tipoFormulario: string
  nombreContratante: string
  numeroPoliza: string
  correo: string
  expiraEnUtc: string
}

export function useEnlaceEnrolamiento() {
  const [contexto, setContexto] = useState<ContextoEnlace | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const datos = new URLSearchParams(window.location.search).get('datos')
    if (!datos) { setError('Enlace incompleto.'); return }

    // Quita ?datos de la barra de direcciones (historial, capturas, referer)
    window.history.replaceState(null, '', window.location.pathname)

    fetch('api/enlace/validar', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ datos }),
    })
      .then(async (r) => {
        if (!r.ok) throw new Error((await r.json()).detail ?? 'El enlace no es válido.')
        return r.json() as Promise<ContextoEnlace>
      })
      .then(setContexto)
      .catch((e) => setError(e.message))
  }, [])

  // Precargar: tipoFormulario (seleccionar en el combo), nombreContratante y numeroPoliza en SOLO LECTURA; correo precargado.
  return { contexto, error }
}
