import { useCallback, useEffect, useState } from "react";
import { getReferenceRates } from "../../../api/planning.js";
import { isAbortError } from "../../../api/client.js";

export default function useReferenceRates() {
  const [state, setState] = useState({ status: "loading", rates: null });
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    getReferenceRates({ signal: controller.signal })
      .then((rates) => setState({ status: "ready", rates }))
      .catch((err) => {
        if (!isAbortError(err)) setState({ status: "error", rates: null });
      });
    return () => controller.abort();
  }, [reloadKey]);

  const retry = useCallback(() => {
    setState({ status: "loading", rates: null });
    setReloadKey((key) => key + 1);
  }, []);

  return { ...state, retry };
}
