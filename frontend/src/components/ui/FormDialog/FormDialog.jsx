import { useCallback, useRef } from "react";
import useModalBehavior from "../../../hooks/useModalBehavior.js";
import { CloseIcon } from "../icons/FeatureIcons.jsx";
import "./FormDialog.css";

// Mesma casca do ConfirmDialog (trava de scroll, Esc fecha, foco preso no painel), mas com um
// <form> dentro. Genérico de propósito: não sabe o que está sendo cadastrado. As ações são botões
// próprios, compactos: o Button global é o de chamada da landing (caixa alta, pílula) e pesa demais
// dentro de um formulário.
export default function FormDialog({
  open,
  title,
  description,
  error,
  submitLabel = "Salvar",
  cancelLabel = "Cancelar",
  submitting = false,
  onSubmit,
  onClose,
  showCloseButton = false,
  closeButtonPlacement = "end",
  children,
}) {
  const panelRef = useRef(null);
  // Durante o envio, fundo e Esc não fecham (os botões já ficam desabilitados): fechar no meio esconderia o
  // resultado ou o erro de uma gravação que continua.
  const close = useCallback(() => { if (!submitting) onClose?.(); }, [submitting, onClose]);
  useModalBehavior(open, close, panelRef);

  if (!open) return null;

  return (
    <div className="form-dialog-overlay" onClick={close}>
      <div
        ref={panelRef}
        className={`form-dialog${showCloseButton ? ` form-dialog--closable form-dialog--close-${closeButtonPlacement}` : ""}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby="form-dialog-title"
        aria-describedby={description ? "form-dialog-description" : undefined}
        tabIndex={-1}
        onClick={(e) => e.stopPropagation()}
      >
        {showCloseButton && (
          <button type="button" className="form-dialog__close" aria-label="Fechar" onClick={onClose} disabled={submitting}>
            <CloseIcon />
          </button>
        )}
        <header className="form-dialog__header">
          <h2 id="form-dialog-title" className="form-dialog__title">
            {title}
          </h2>
          {description && (
            <p id="form-dialog-description" className="form-dialog__description">
              {description}
            </p>
          )}
        </header>

        <form className="form-dialog__form" onSubmit={onSubmit} aria-busy={submitting}>
          <div className="form-dialog__body">{children}</div>

          {error && (
            <p className="form-dialog__error" role="alert">
              {error}
            </p>
          )}

          <div className="form-dialog__actions">
            {cancelLabel && (
              <button type="button" className="form-dialog__action form-dialog__action--ghost" onClick={onClose} disabled={submitting}>
                {cancelLabel}
              </button>
            )}
            <button type="submit" className="form-dialog__action form-dialog__action--primary" disabled={submitting}>
              {submitting ? "Salvando…" : submitLabel}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
