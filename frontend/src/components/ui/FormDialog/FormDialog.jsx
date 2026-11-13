import { useRef } from "react";
import useModalBehavior from "../../../hooks/useModalBehavior.js";
import Button from "../Button/Button.jsx";
import "./FormDialog.css";

// Mesma casca do ConfirmDialog (trava de scroll, Esc fecha, foco preso no painel), mas com um
// <form> dentro: o ConfirmDialog só tem título + mensagem + dois botões e não serve pra entrada
// de dados. Genérico de propósito: não sabe o que está sendo cadastrado.
export default function FormDialog({
  open,
  title,
  description,
  error,
  submitLabel = "Salvar",
  submitting = false,
  onSubmit,
  onClose,
  children,
}) {
  const panelRef = useRef(null);
  useModalBehavior(open, onClose, panelRef);

  if (!open) return null;

  return (
    <div className="form-dialog-overlay" onClick={onClose}>
      <div
        ref={panelRef}
        className="form-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="form-dialog-title"
        tabIndex={-1}
        onClick={(e) => e.stopPropagation()}
      >
        <h2 id="form-dialog-title" className="form-dialog__title">
          {title}
        </h2>
        {description && <p className="form-dialog__description">{description}</p>}

        <form className="form-dialog__form" onSubmit={onSubmit}>
          {children}

          {error && (
            <p className="form-dialog__error" role="alert">
              {error}
            </p>
          )}

          <div className="form-dialog__actions">
            <Button as="button" type="button" variant="outline" size="sm" onClick={onClose} disabled={submitting}>
              Cancelar
            </Button>
            <Button as="button" type="submit" variant="primary" size="sm" disabled={submitting}>
              {submitting ? "Salvando..." : submitLabel}
            </Button>
          </div>
        </form>
      </div>
    </div>
  );
}
