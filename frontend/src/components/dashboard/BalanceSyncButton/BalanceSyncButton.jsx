import { useEffect, useRef, useState } from "react";
import { syncConnectionBalance } from "../../../api/polp.js";
import { useAccount } from "../../../context/AccountContext.jsx";
import { SyncIcon } from "../../ui/icons/FeatureIcons.jsx";
import "./BalanceSyncButton.css";

const FEEDBACK_DURATION_MS = 4000;

function resultMessage(result) {
  if (result?.failed > 0) return "Algumas contas não foram atualizadas";
  if (result?.skipped > 0 && result?.updated === 0) return "Saldo já atualizado há pouco";
  return "Saldo atualizado";
}

// Usa sempre a conexão da conta escolhida no seletor global (effectiveAccountId), nunca o cartão
// escolhido em Cartões, que é uma seleção só daquela aba.
export default function BalanceSyncButton() {
  const { accounts, effectiveAccountId, bumpDataRefresh } = useAccount();
  const [state, setState] = useState(null); // { connectionId, status: "loading" | "done" | "error", message }
  const timerRef = useRef(null);

  useEffect(() => () => clearTimeout(timerRef.current), []);

  const activeAccount = accounts.find((a) => a.id === effectiveAccountId);
  const connectionId = activeAccount?.localIntegrationId ?? null;
  const canSync =
    connectionId != null && accounts.some((a) => a.localIntegrationId === connectionId && a.canSyncBalance);

  if (!canSync) return null;

  const current = state?.connectionId === connectionId ? state : null;
  const loading = current?.status === "loading";

  async function handleClick() {
    clearTimeout(timerRef.current);
    setState({ connectionId, status: "loading" });
    try {
      const result = await syncConnectionBalance(connectionId);
      setState({ connectionId, status: "done", message: resultMessage(result), partial: result?.failed > 0 });
      bumpDataRefresh();
    } catch (err) {
      setState({ connectionId, status: "error", message: err.message || "Não foi possível atualizar o saldo agora." });
    }
    timerRef.current = setTimeout(() => setState(null), FEEDBACK_DURATION_MS);
  }

  const tone = current?.status === "error" || (current?.status === "done" && current.partial) ? "error" : "info";
  const message = current && current.status !== "loading" ? current.message : "";

  return (
    <div className="balance-sync">
      <button
        type="button"
        className="balance-sync__button"
        onClick={handleClick}
        disabled={loading}
        aria-busy={loading || undefined}
      >
        <span className={`balance-sync__icon${loading ? " balance-sync__icon--spinning" : ""}`} aria-hidden="true">
          <SyncIcon />
        </span>
        {loading ? "Atualizando..." : "Atualizar saldo"}
      </button>
      <span className="balance-sync__live" role="status">
        {message && (
          <span className={`balance-sync__feedback balance-sync__feedback--${tone}`}>{message}</span>
        )}
      </span>
    </div>
  );
}
