import { useCallback, useEffect, useState } from "react";
import { loadPlanning } from "../../../api/planning.js";
import { isAbortError } from "../../../api/client.js";
import { useAuth } from "../../../context/AuthContext.jsx";

const INITIAL = { status: "loading", reserve: null, goals: [], rateProfiles: {} };

function indexProfiles(list) {
  return Object.fromEntries((list ?? []).map((profile) => [profile.bankAccountId, profile]));
}

const fromServer = (planning) => ({ reserve: planning.reserve ?? null, goals: planning.goals ?? [], rateProfiles: indexProfiles(planning.rateProfiles) });

export default function usePlanning() {
  const owner = useAuth()?.user?.id;
  const [state, setState] = useState(() => ({ ...INITIAL, owner }));
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setState((prev) => prev.owner === owner ? prev : { ...INITIAL, owner });
    loadPlanning({ signal: controller.signal })
      .then((planning) => {
        if (controller.signal.aborted) return;
        setState({ owner, status: "ready", ...fromServer(planning) });
      })
      .catch((err) => {
        if (!isAbortError(err) && !controller.signal.aborted) setState((prev) => ({ ...prev, status: "error" }));
      });
    return () => controller.abort();
  }, [reloadKey, owner]);

  const retry = useCallback(() => {
    setState((prev) => ({ ...prev, status: "loading" }));
    setReloadKey((key) => key + 1);
  }, []);

  // Respostas de vínculo trazem o planejamento inteiro: substitui de uma vez reserva e metas.
  const replace = useCallback((planning) => setState((prev) => prev.owner === owner ? { ...prev, ...fromServer(planning) } : prev), [owner]);
  const updateGoals = useCallback((update) => setState((prev) => prev.owner === owner ? { ...prev, goals: update(prev.goals) } : prev), [owner]);
  const setRateProfile = useCallback(
    (accountId, profile) =>
      setState((prev) => {
        if (prev.owner !== owner) return prev;
        const rateProfiles = { ...prev.rateProfiles };
        if (profile) rateProfiles[accountId] = profile;
        else delete rateProfiles[accountId];
        return { ...prev, rateProfiles };
      }),
    [owner],
  );

  return { ...(state.owner === owner ? state : INITIAL), retry, replace, updateGoals, setRateProfile };
}
