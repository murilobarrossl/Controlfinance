import { useCallback, useEffect, useRef, useState } from "react";
import Button from "../../components/ui/Button/Button.jsx";
import { TermosDeUsoTexto, TERMS_UPDATED_AT } from "../legal/TermosDeUso.jsx";
import "../legal/LegalPage.css";
import "./TermsDialog.css";

const BOTTOM_TOLERANCE_PX = 8;

export default function TermsDialog({ submitting, onAccept, onClose }) {
  const panelRef = useRef(null);
  const contentRef = useRef(null);
  const [scrolledToEnd, setScrolledToEnd] = useState(false);
  const [fitsWithoutScroll, setFitsWithoutScroll] = useState(false);
  const reachedEnd = scrolledToEnd || fitsWithoutScroll;

  const handleScroll = useCallback(() => {
    const el = contentRef.current;
    if (!el || el.scrollTop === 0) return;
    if (el.scrollTop + el.clientHeight >= el.scrollHeight - BOTTOM_TOLERANCE_PX) setScrolledToEnd(true);
  }, []);

  useEffect(() => {
    const el = contentRef.current;
    if (!el) return;

    const observer = new ResizeObserver(() => {
      setFitsWithoutScroll(el.scrollHeight <= el.clientHeight + BOTTOM_TOLERANCE_PX);
    });
    observer.observe(el);
    if (el.firstElementChild) observer.observe(el.firstElementChild);

    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    panelRef.current?.focus();

    function handleKeyDown(e) {
      if (e.key === "Escape") onClose();
    }
    document.addEventListener("keydown", handleKeyDown);

    return () => {
      document.body.style.overflow = previousOverflow;
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [onClose]);

  // Links do texto (ex.: Política de Privacidade) abririam na mesma aba e apagariam o formulário.
  function handleContentClick(e) {
    const link = e.target.closest("a[href]");
    if (!link) return;
    e.preventDefault();
    window.open(link.href, "_blank", "noopener,noreferrer");
  }

  return (
    <div className="terms-dialog-overlay" onClick={onClose}>
      <div
        ref={panelRef}
        className="terms-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="terms-dialog-title"
        tabIndex={-1}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="terms-dialog__header">
          <h3 id="terms-dialog-title" className="terms-dialog__title">
            Termos de uso
          </h3>
          <p className="terms-dialog__updated">Última atualização: {TERMS_UPDATED_AT}</p>
        </div>

        <div
          ref={contentRef}
          className="terms-dialog__content legal-page__content"
          onScroll={handleScroll}
          onClick={handleContentClick}
          tabIndex={0}
        >
          <div>
            <TermosDeUsoTexto />
          </div>
        </div>

        <p className="terms-dialog__hint" aria-live="polite">
          {reachedEnd ? "Você chegou ao final dos Termos." : "Role até o final para poder aceitar."}
        </p>

        <div className="terms-dialog__actions">
          <Button as="button" type="button" variant="outline" size="sm" onClick={onClose}>
            Voltar
          </Button>
          <Button
            as="button"
            type="button"
            variant="primary"
            size="sm"
            onClick={onAccept}
            disabled={!reachedEnd || submitting}
          >
            {submitting ? "Criando conta..." : "Li e aceito os Termos"}
          </Button>
        </div>
      </div>
    </div>
  );
}
