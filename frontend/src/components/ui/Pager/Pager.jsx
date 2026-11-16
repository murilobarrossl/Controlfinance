import Skeleton from "../Skeleton/Skeleton.jsx";
import "./Pager.css";

// Mesma paginação de Relatórios (contagem à esquerda, "Página X de Y" entre ‹ e ›), como componente
// para as outras listas paginadas no servidor.
export default function Pager({ page, totalPages, totalCount, countLabel, onChange, loading = false, label }) {
  return (
    <nav className="pager" aria-label={label}>
      <span className="pager__count">
        {loading && totalCount == null ? <Skeleton width="2rem" /> : totalCount} {countLabel}
      </span>
      <div className="pager__controls">
        <button type="button" onClick={() => onChange(page - 1)} disabled={page <= 1} aria-label="Página anterior">
          ‹
        </button>
        <span aria-live="polite">
          Página {page} de {loading && totalPages == null ? <Skeleton width="1.5rem" /> : totalPages}
        </span>
        <button
          type="button"
          onClick={() => onChange(page + 1)}
          disabled={loading || totalPages == null || page >= totalPages}
          aria-label="Próxima página"
        >
          ›
        </button>
      </div>
    </nav>
  );
}
