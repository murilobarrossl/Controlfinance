import { useEffect, useRef, useState } from "react";
import Button from "../../../components/ui/Button/Button.jsx";
import "../../../components/ui/ConfirmDialog/ConfirmDialog.css";

export default function DeleteAccountDialog({ deleting, error, onConfirm, onClose }) {
  const passwordRef = useRef(null);
  const [password, setPassword] = useState("");
  const [confirmed, setConfirmed] = useState(false);

  useEffect(() => {
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    passwordRef.current?.focus();

    function handleKeyDown(e) {
      if (e.key === "Escape") onClose();
    }
    document.addEventListener("keydown", handleKeyDown);

    return () => {
      document.body.style.overflow = previousOverflow;
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [onClose]);

  function handleSubmit(e) {
    e.preventDefault();
    onConfirm(password);
  }

  return (
    <div className="confirm-dialog-overlay" onClick={onClose}>
      <form
        className="confirm-dialog privacidade__delete-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="delete-account-title"
        onClick={(e) => e.stopPropagation()}
        onSubmit={handleSubmit}
      >
        <h3 id="delete-account-title" className="confirm-dialog__title">
          Excluir sua conta?
        </h3>
        <p className="confirm-dialog__message">
          Para confirmar, digite sua senha. Depois de excluída, a conta não pode ser recuperada.
        </p>

        <div className="privacidade__delete">
          <label className="privacidade__field">
            <span>Sua senha</span>
            <input
              ref={passwordRef}
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
              required
            />
          </label>
          <label className="privacidade__confirm">
            <input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} required />
            <span>Entendo que a exclusão é definitiva.</span>
          </label>
          {error && <p className="privacidade__error">{error}</p>}
        </div>

        <div className="confirm-dialog__actions">
          <Button as="button" type="button" variant="outline" size="sm" onClick={onClose} disabled={deleting}>
            Cancelar
          </Button>
          <Button as="button" type="submit" variant="primary" size="sm" disabled={!confirmed || !password || deleting}>
            {deleting ? "Excluindo..." : "Excluir definitivamente"}
          </Button>
        </div>
      </form>
    </div>
  );
}
