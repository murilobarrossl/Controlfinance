import { useCallback, useEffect, useState } from "react";
import { loadPlanning } from "../../../api/planning.js";
import { isAbortError } from "../../../api/client.js";
import { useAuth } from "../../../context/AuthContext.jsx";

const INITIAL = { status: "loading", reserve: null, goals: [], rateProfiles: {}, legacyReserveAmount: null };

function indexProfiles(list) {
  return Object.fromEntries((list ?? []).map((profile) => [profile.bankAccountId, profile]));
}

export default function usePlanning() {
  const owner = useAuth()?.user?.id;
  const [state, setState] = useState(() => ({ ...INITIAL, owner }));
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setState((prev) => prev.owner === owner ? prev : { ...INITIAL, owner });
    loadPlanning({ signal: controller.signal })
      .then(({ planning, legacyReserveAmount }) => {
        if (controller.signal.aborted) return;
        setState({
          owner,
          status: "ready",
          reserve: planning.reserve ?? null,
          goals: planning.goals ?? [],
          rateProfiles: indexProfiles(planning.rateProfiles),
          legacyReserveAmount,
        });
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

  const setReserve = useCallback((reserve) => setState((prev) => prev.owner === owner ? { ...prev, reserve } : prev), [owner]);
  const updateGoals = useCallback((update) => setState((prev) => prev.owner === owner ? { ...prev, goals: update(prev.goals) } : prev), [owner]);
  const clearLegacyReserveNotice = useCallback(() => setState((prev) => prev.owner === owner ? { ...prev, legacyReserveAmount: null } : prev), [owner]);
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

  return { ...(state.owner === owner ? state : INITIAL), retry, setReserve, updateGoals, clearLegacyReserveNotice, setRateProfile };
}
