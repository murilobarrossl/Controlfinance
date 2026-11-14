import { useEffect, useRef, useState } from "react";
import { investmentDetail, investmentHistory, investmentMovements, investmentFinancials, isThrottled } from "../../../api/investments.js";
import { isAbortError } from "../../../api/client.js";

const empty = () => ({ data: [], cursor: null, status: "loading", loadingMore: false, moreError: false });
export function useInvestmentResource(item, owner, month, refreshKey) {
  const [reload, setReload] = useState(0);
  const [state, setState] = useState(null);
  const [financial, setFinancial] = useState(null);
  const request = useRef(null);
  const generation = useRef(0);
  const moreLoaded = useRef(false);
  const busy = useRef(new Set());
  const key = `${owner}:${item.key}`;
  useEffect(() => {
    const current = ++generation.current;
    const controller = new AbortController(); request.current = controller;
    moreLoaded.current = false; busy.current.clear();
    setState((prev) => prev?.key === key ? { ...prev, loading: true, error: false,
      history: { ...prev.history, refreshing: true, loadingMore: false }, movements: { ...prev.movements, refreshing: true, loadingMore: false } }
      : { key, loading: true, error: false, history: empty(), movements: empty() });
    investmentDetail(item.id, controller.signal, item.reserved).then((resource) => {
      if (!controller.signal.aborted) setState((prev) => ({ ...prev, resource, loading: false, throttled: false }));
    }).catch((error) => {
      if (isAbortError(error) || controller.signal.aborted) return;
      setState((prev) => prev.resource && isThrottled(error) ? { ...prev, loading: false, throttled: true }
        : { ...prev, loading: false, error: true, resource: null, removed: error.status === 404 });
    });
    for (const kind of item.reserved ? ["history"] : ["history", "movements"]) {
      const promise = kind === "history" ? investmentHistory(item.id, item.reserved, null, controller.signal) : investmentMovements(item.id, null, controller.signal);
      promise.then((result) => {
        if (!Array.isArray(result.data)) throw new Error("Invalid investment data");
        if (!controller.signal.aborted && generation.current === current)
          setState((prev) => ({ ...prev, [kind]: { ...empty(), data: result.data, cursor: result.nextCursor ?? null, status: "ready" } }));
      }).catch((error) => {
        if (isAbortError(error) || controller.signal.aborted) return;
        setState((prev) => prev[kind]?.status === "ready" && isThrottled(error) ? { ...prev, [kind]: { ...prev[kind], refreshing: false } }
          : { ...prev, [kind]: { ...empty(), status: "error" } });
      });
    }
    const timer = setInterval(() => { if (document.visibilityState === "visible" && !moreLoaded.current && busy.current.size === 0) setReload((n) => n + 1); }, 60000);
    return () => { controller.abort(); generation.current += 1; clearInterval(timer); };
  }, [key, item.id, item.reserved, reload, refreshKey]);

  useEffect(() => {
    if (item.reserved) return;
    const controller = new AbortController();
    setFinancial((prev) => prev?.key === key && prev.month === month ? { ...prev, status: "loading" } : { key, month, status: "loading" });
    investmentFinancials(item.id, month, controller.signal).then((data) => {
      if (!controller.signal.aborted) setFinancial({ key, month, status: "ready", data });
    }).catch((error) => {
      if (!isAbortError(error) && !controller.signal.aborted) setFinancial((prev) => prev?.data && prev.key === key && prev.month === month && isThrottled(error) ? { ...prev, status: "ready" } : { key, month, status: "error" });
    });
    return () => controller.abort();
  }, [key, item.id, item.reserved, month, reload, refreshKey]);

  async function more(kind) {
    const dataset = state?.[kind];
    if (state?.key !== key || !dataset?.cursor || dataset.refreshing || busy.current.has(kind)) return;
    busy.current.add(kind); const current = generation.current;
    setState((prev) => ({ ...prev, [kind]: { ...prev[kind], loadingMore: true, moreError: false } }));
    try {
      const result = kind === "history" ? await investmentHistory(item.id, item.reserved, dataset.cursor, request.current.signal)
        : await investmentMovements(item.id, dataset.cursor, request.current.signal);
      if (generation.current !== current) return;
      if (!Array.isArray(result.data) || result.nextCursor === dataset.cursor) throw new Error("Invalid investment pagination");
      moreLoaded.current = true;
      setState((prev) => ({ ...prev, [kind]: { ...prev[kind], data: [...new Map([...prev[kind].data, ...result.data].map((row) => [row.id, row])).values()],
        cursor: result.nextCursor ?? null, loadingMore: false } }));
    } catch (error) {
      if (!isAbortError(error) && generation.current === current) setState((prev) => ({ ...prev, [kind]: { ...prev[kind], loadingMore: false, moreError: true } }));
    } finally { if (generation.current === current) busy.current.delete(kind); }
  }
  return { ...(state?.key === key ? state : { loading: true, history: empty(), movements: empty() }),
    financial: financial?.key === key && financial.month === month ? financial : { status: "loading" },
    more, retry: () => setReload((n) => n + 1) };
}
