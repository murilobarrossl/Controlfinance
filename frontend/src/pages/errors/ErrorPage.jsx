import { useEffect, useId, useRef } from "react";
import { Link } from "react-router";
import { useAuth } from "../../context/AuthContext.jsx";
import Button from "../../components/ui/Button/Button.jsx";
import logo from "../../assets/images/cflogobranco.svg";
import "./ErrorPage.css";

const ERRORS = {
  403: {
    title: "Acesso não permitido",
    description: "Você não tem permissão para acessar este conteúdo. Volte para continuar usando o aplicativo.",
  },
  404: {
    title: "Página não encontrada",
    description: "Este endereço não existe ou a página foi movida. Use o botão abaixo para continuar.",
  },
  500: {
    title: "Algo deu errado",
    description: "Não conseguimos abrir esta página. Você pode recarregá-la ou voltar para continuar.",
    retry: true,
  },
  503: {
    title: "Serviço temporariamente indisponível",
    description: "Não conseguimos acessar o serviço agora. Verifique sua conexão ou tente novamente em alguns instantes.",
    retry: true,
  },
};

export function PageLoading({ embedded = false }) {
  return (
    <div className={`page-loading${embedded ? " page-loading--embedded" : ""}`} role="status">
      <span className="page-loading__indicator" aria-hidden="true" />
      <span>Carregando página…</span>
    </div>
  );
}

export default function ErrorPage({ code = 500, embedded = false, onRetry, onNavigate }) {
  const { isAuthenticated, status, retrySession } = useAuth();
  const headingId = useId();
  const heading = useRef(null);
  const errorCode = Object.hasOwn(ERRORS, code) ? code : 500;
  const error = ERRORS[errorCode];
  const destination = isAuthenticated ? "/dashboard" : "/";
  const returnLabel = isAuthenticated ? "Voltar ao Dashboard inteligente" : "Voltar à página inicial";
  const Container = embedded ? "section" : "main";

  function retryUnavailable(event) {
    if (status === "error") return retrySession();
    if (onRetry) return onRetry(event);
    // A rota dedicada não representa a página que falhou: recarregá-la exibiria outro 503.
    if (window.location.pathname === "/503") return window.location.replace(destination);
    window.location.reload();
  }

  useEffect(() => {
    const previousTitle = document.title;
    document.title = `${errorCode} · ${error.title} | Control Finance`;
    heading.current?.focus();
    return () => { document.title = previousTitle; };
  }, [errorCode, error.title]);

  return (
    <Container className={`error-page${embedded ? " error-page--embedded" : ""}`} aria-labelledby={headingId}>
      {!embedded && <img className="error-page__logo" src={logo} alt="Control Finance" />}
      <div className="error-page__content">
        <p className="error-page__code" aria-hidden="true">{errorCode}</p>
        <h1 id={headingId} ref={heading} tabIndex={-1} className="error-page__title">{error.title}</h1>
        <p className="error-page__description">{error.description}</p>
        <div className={`error-page__actions${errorCode === 503 ? " error-page__actions--single" : ""}`}>
          {errorCode === 503 ? (
            <Button
              type="button"
              disabled={status === "loading"}
              onClick={retryUnavailable}
            >
              {status === "loading" ? "Tentando novamente…" : "Tentar novamente"}
            </Button>
          ) : (
            <>
              {status === "loading" ? (
                <Button type="button" disabled>Verificando sessão…</Button>
              ) : status === "error" ? (
                <Button type="button" onClick={retrySession}>Verificar sessão novamente</Button>
              ) : (
                <Button as={Link} to={destination} replace onClick={onNavigate}>{returnLabel}</Button>
              )}
              {error.retry && (
                <Button type="button" variant="outline" onClick={status === "error" ? retrySession : onRetry ?? (() => window.location.reload())}>
                  Tentar novamente
                </Button>
              )}
            </>
          )}
        </div>
      </div>
    </Container>
  );
}
