export type Paso = 'carga' | 'revision' | 'correo' | 'envio'

const pasos: { id: Paso; titulo: string }[] = [
  { id: 'carga', titulo: 'Cargar archivo' },
  { id: 'revision', titulo: 'Revisar datos' },
  { id: 'correo', titulo: 'Preparar correo' },
  { id: 'envio', titulo: 'Envío' },
]

export function Pasos({ actual, habilitados, onIr }: { actual: Paso; habilitados: Paso[]; onIr: (p: Paso) => void }) {
  const idx = pasos.findIndex((p) => p.id === actual)
  return (
    <nav className="pasos" aria-label="Pasos">
      <ol>
        {pasos.map((p, i) => (
          <li key={p.id} className={i === idx ? 'actual' : i < idx ? 'hecho' : ''}>
            <button type="button" disabled={!habilitados.includes(p.id) || i === idx} onClick={() => onIr(p.id)}
              aria-current={i === idx ? 'step' : undefined}>
              <span className="paso-numero">{i < idx ? '✓' : i + 1}</span>
              <span className="paso-titulo">{p.titulo}</span>
            </button>
          </li>
        ))}
      </ol>
    </nav>
  )
}
