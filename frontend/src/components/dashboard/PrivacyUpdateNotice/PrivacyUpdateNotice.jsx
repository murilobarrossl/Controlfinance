import { useState } from "react";
import { Link } from "react-router";
import { useAuth } from "../../../context/AuthContext.jsx";
import { acknowledgePrivacy } from "../../../api/account.js";
import { PRIVACY_VERSION } from "../../../legal/versions.js";

// Some só depois que o backend registra a ciência (privacy_notice) da versão exibida. Não registra
// consentimento nem aceite: esses têm fluxo próprio.
export default function PrivacyUpdateNotice() {
  const { user, updateUser } = useAuth();
  const [state, setState] = useState({ sending: false, error: "" });
  if (!user?.pendingPrivacyVersion || user.pendingPrivacyVersion !== PRIVACY_VERSION) return null;

  async function acknowledge() {
    setState({ sending: true, error: "" });
    try {
      await acknowledgePrivacy(PRIVACY_VERSION);
      updateUser({ ...user, pendingPrivacyVersion: null });
    } catch (err) {
      setState({ sending: false, error: err.message || "Não foi possível registrar. Tente de novo." });
    }
  }

  return (
    <div className="dashboard-notice" role="status">
      <p>
        Atualizamos a <Link to="/politica-de-privacidade">Política de Privacidade</Link> (versão {PRIVACY_VERSION}).
        {state.error && ` ${state.error}`}
      </p>
      <button type="button" className="dashboard-notice__ack" onClick={acknowledge} disabled={state.sending}>
        {state.sending ? "Aguarde..." : "Entendi"}
      </button>
    </div>
  );
}
