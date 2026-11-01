import { useRef } from "react";
import useModalBehavior from "../../../hooks/useModalBehavior.js";
import Button from "../Button/Button.jsx";
import "./ConfirmDialog.css";

// Genérico de propósito (não sabe nada de cartão/conta): mesmo padrão de trava de scroll +
// Esc-fecha já usado pra sidebar mobile em DashboardLayout.jsx, só que aqui "fora" é o overlay
// inteiro, então clique-fora não precisa do listener de mousedown+ref que o AccountSwitcher usa.
export default function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = "Confirmar",
  cancelLabel = "Cancelar",
  onConfirm,
  onClose,
}) {
  const panelRef = useRef(null);

  useModalBehavior(open, onClose, panelRef);

  if (!open) return null;

  return (
    <div className="confirm-dialog-overlay" onClick={onClose}>
      <div
        ref={panelRef}
        className="confirm-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirm-dialog-title"
        tabIndex={-1}
        onClick={(e) => e.stopPropagation()}
      >
        <h3 id="confirm-dialog-title" className="confirm-dialog__title">
          {title}
        </h3>
        <p className="confirm-dialog__message">{message}</p>

        <div className="confirm-dialog__actions">
          <Button as="button" type="button" variant="outline" size="sm" onClick={onClose}>
            {cancelLabel}
          </Button>
          <Button as="button" type="button" variant="primary" size="sm" onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </div>
      </div>
    </div>
  );
}
