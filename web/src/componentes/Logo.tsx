import { useState } from 'react'

// Coloque el logo oficial en web/public/logo-ficohsa.png (o .svg). Mientras no exista se muestra el nombre.
const candidatos = ['./logo-ficohsa.png', './logo-ficohsa.svg']

export function Logo({ className }: { className?: string }) {
  const [i, setI] = useState(0)
  // El texto de respaldo no toma la clase de la imagen (tamaños y márgenes son para el PNG).
  if (i >= candidatos.length) return <span className="marca-texto">FICOHSA</span>
  return <img src={candidatos[i]} alt="Ficohsa Seguros" className={className} onError={() => setI(i + 1)} />
}
