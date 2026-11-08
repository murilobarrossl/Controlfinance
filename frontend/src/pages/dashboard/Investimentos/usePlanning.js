import { useCallback, useEffect, useState } from "react";
import { loadPlanning } from "../../../api/planning.js";
import { isAbortError } from "../../../api/client.js";

export default function usePlanning() {
  const [state, setState] = useState({ status: "loading", reserve: null, goals: [], legacyReserveAmount: null });
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    loadPlanning({ signal: controller.signal })
      .then(({ planning, legacyReserveAmount }) =>
        setState({ status: "ready", reserve: planning.reserve ?? null, goals: planning.goals ?? [], legacyReserveAmount }),
      )
      .catch((err) => {
        if (!isAbortError(err)) setState((prev) => ({ ...prev, status: "error" }));
      });
    return () => controller.abort();
  }, [reloadKey]);

  const retry = useCallback(() => {
    setState((prev) => ({ ...prev, status: "loading" }));
    setReloadKey((key) => key + 1);
  }, []);

  const setReserve = useCallback((reserve) => setState((prev) => ({ ...prev, reserve })), []);
  const updateGoals = useCallback((update) => setState((prev) => ({ ...prev, goals: update(prev.goals) })), []);
  const clearLegacyReserveNotice = useCallback(() => setState((prev) => ({ ...prev, legacyReserveAmount: null })), []);

  return { ...state, retry, setReserve, updateGoals, clearLegacyReserveNotice };
}
