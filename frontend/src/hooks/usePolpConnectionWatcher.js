import { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { getIntegrationStatus, syncIntegration } from "../api/polp.js";

const PENDING_KEY = "pendingPolpIntegrationId";
const POLL_MAX_ATTEMPTS = 30;
const POLL_INTERVAL_MS = 2000;

function wait(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export default function usePolpConnectionWatcher() {
  const navigate = useNavigate();

  useEffect(() => {
    const integrationId = localStorage.getItem(PENDING_KEY);
    if (!integrationId) return;

    let cancelled = false;

    async function watch() {
      for (let attempt = 0; attempt < POLL_MAX_ATTEMPTS && !cancelled; attempt++) {
        try {
          const status = await getIntegrationStatus(integrationId);

          if (status?.status === "active") {
            await syncIntegration(integrationId);
            localStorage.removeItem(PENDING_KEY);
            if (!cancelled) navigate("/dashboard");
            return;
          }

          if (status?.status === "login_error") {
            localStorage.removeItem(PENDING_KEY);
            return;
          }
        } catch {
          // ignora falhas pontuais de rede e tenta novamente até esgotar as tentativas
        }

        await wait(POLL_INTERVAL_MS);
      }

      localStorage.removeItem(PENDING_KEY);
    }

    watch();

    return () => {
      cancelled = true;
    };
  }, [navigate]);
}
