import { Component } from "react";
import ErrorPage from "../../pages/errors/ErrorPage.jsx";

export default class ErrorBoundary extends Component {
  state = { errorCode: null };

  static getDerivedStateFromError(error) {
    const status = Number(error?.status);
    return { errorCode: [403, 404, 503].includes(status) ? status : 500 };
  }

  componentDidCatch(error, info) {
    console.error("Erro não tratado na interface:", error, info);
  }

  componentDidUpdate(previousProps) {
    if (this.state.errorCode && previousProps.resetKey !== this.props.resetKey) {
      this.resetError();
    }
  }

  resetError = () => {
    this.setState({ errorCode: null });
  };

  handleReload = () => {
    window.location.reload();
  };

  render() {
    if (this.state.errorCode) {
      return (
        <ErrorPage code={this.state.errorCode} embedded={this.props.embedded}
          onRetry={this.handleReload} onNavigate={this.resetError} />
      );
    }
    return this.props.children;
  }
}
