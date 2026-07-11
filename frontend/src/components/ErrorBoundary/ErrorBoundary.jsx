import { Component } from "react";
import "./ErrorBoundary.css";

export default class ErrorBoundary extends Component {
  state = { hasError: false };

  static getDerivedStateFromError() {
    return { hasError: true };
  }

  componentDidCatch(error, info) {
    console.error("Erro não tratado no dashboard:", error, info);
  }

  handleReload = () => {
    this.setState({ hasError: false });
    window.location.reload();
  };

  render() {
    if (this.state.hasError) {
      return (
        <div className="error-boundary">
          <h2 className="error-boundary__title">Algo deu errado</h2>
          <p className="error-boundary__text">
            Não conseguimos carregar esta página. Tente recarregar — se o problema continuar,
            entre em contato com o suporte.
          </p>
          <button type="button" className="error-boundary__button" onClick={this.handleReload}>
            Recarregar página
          </button>
        </div>
      );
    }

    return this.props.children;
  }
}
