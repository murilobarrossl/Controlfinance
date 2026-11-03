import { useEffect } from "react";
import { useNavigate } from "react-router";
import { useAuth } from "../context/AuthContext.jsx";
import { getPendingIntegrationId, watchPolpConnection } from "../services/polpConnection.js";

export default function usePolpConnectionWatcher() {
  const navigate = useNavigate();
  const { isAuthenticated } = useAuth();

  useEffect(() => {
    if (!isAuthenticated) return;

    const integrationId = getPendingIntegrationId();
    if (!integrationId) return;

    let cancelled = false;

    watchPolpConnection(integrationId, { isCancelled: () => cancelled }).then((result) => {
      if (!cancelled && result === "active") navigate("/dashboard");
    });

    return () => {
      cancelled = true;
    };
  }, [navigate, isAuthenticated]);
}
