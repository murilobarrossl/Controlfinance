import { useCallback, useEffect, useRef, useState } from "react";
import { investmentPage } from "../../../api/investments.js";
import { isAbortError } from "../../../api/client.js";

const KINDS = ["investments", "reserved-balances"];
const empty = () => ({ data: [], cursor: null, status: "loading", moreError: false, loadingMore: false, pages: 0 });
const initial = (owner, scope) => ({ owner, scope, ...Object.fromEntries(KINDS.map((kind) => [kind, empty()])) });
export default function useImportedInvestments(owner, refreshKey, integrationId = null) {
  const [state, setState] = useState(() => initial(owner, integrationId));
  const [reload, setReload] = useState(0);
  const generation = useRef(0);
  const controller = useRef(null);
  const latest = useRef(state);
  useEffect(() => { latest.current = state; }, [state]);
  const refreshing = useRef(false);
  const inFlight = useRef(new Set());
  const retry = useCallback(() => setReload((key) => key + 1), []);
  useEffect(() => {
    const current = ++generation.current;
    const request = new AbortController(); controller.current = request;
    const previous = latest.current.owner === owner && latest.current.scope === integrationId ? latest.current : null;
    inFlight.current.clear(); refreshing.current = true;
    setState((prev) => prev.owner === owner && prev.scope === integrationId ? { ...prev,
      ...Object.fromEntries(KINDS.map((kind) => [kind, { ...prev[kind], refreshing: true, loadingMore: false }])) } : initial(owner, integrationId));
    // Refresh the window the user loaded, rather than disabling refresh after page two
    // or replacing a multi-page catalog with the first page during a background update.
    const reads = KINDS.map(async (kind) => {
      try {
        const targetPages = Math.max(1, previous?.[kind]?.pages ?? 1);
        const rows = new Map();
        const cursors = new Set();
        let cursor = null; let loadedPages = 0;
        do {
          const result = await investmentPage(kind, cursor, request.signal, integrationId);
          if (request.signal.aborted || generation.current !== current) return;
          if (!Array.isArray(result.data) || result.data.some((row) => !row.id)) throw new Error("Invalid investment page");
          if (integrationId && result.data.some((row) => row.integrationId !== integrationId)) throw new Error("Invalid investment connection");
          for (const row of result.data) rows.set(row.id, row);
          cursor = result.nextCursor ?? null;
          if (cursor !== null && (typeof cursor !== "string" || !cursor || cursors.has(cursor))) throw new Error("Invalid investment pagination");
          if (cursor) cursors.add(cursor);
          loadedPages += 1;
        } while (cursor && loadedPages < targetPages);
        setState((prev) => ({ ...prev, owner, scope: integrationId, [kind]: { ...empty(), data: [...rows.values()], cursor, status: "ready", pages: loadedPages } }));
      } catch (error) {
        if (!isAbortError(error) && generation.current === current && !request.signal.aborted)
          setState((prev) => ({ ...prev, [kind]: { ...prev[kind], status: "error", loadingMore: false, refreshing: false } }));
      }
    });
    Promise.allSettled(reads).then(() => { if (generation.current === current) refreshing.current = false; });
    const visibleRefresh = () => { if (document.visibilityState === "visible" && !refreshing.current && inFlight.current.size === 0) retry(); };
    const timer = setInterval(visibleRefresh, 60000);
    document.addEventListener("visibilitychange", visibleRefresh);
    return () => { request.abort(); generation.current += 1; clearInterval(timer); document.removeEventListener("visibilitychange", visibleRefresh); };
  }, [owner, refreshKey, reload, retry, integrationId]);

  const loadMore = useCallback(async (kind) => {
    const dataset = state[kind];
    if (state.owner !== owner || state.scope !== integrationId || !dataset?.cursor || dataset.refreshing || refreshing.current || inFlight.current.has(kind)) return;
    inFlight.current.add(kind);
    const current = generation.current;
    setState((prev) => ({ ...prev, [kind]: { ...prev[kind], loadingMore: true, moreError: false } }));
    try {
      const result = await investmentPage(kind, dataset.cursor, controller.current.signal, integrationId);
      if (generation.current !== current || controller.current.signal.aborted) return;
      if (!Array.isArray(result.data) || result.data.some((row) => !row.id) || result.nextCursor === dataset.cursor
          || (result.nextCursor != null && (typeof result.nextCursor !== "string" || !result.nextCursor))) throw new Error("Invalid investment pagination");
      if (integrationId && result.data.some((row) => row.integrationId !== integrationId)) throw new Error("Invalid investment connection");
      setState((prev) => ({ ...prev, [kind]: { ...prev[kind], data: [...new Map([...prev[kind].data, ...result.data].map((row) => [row.id, row])).values()],
        cursor: result.nextCursor ?? null, pages: prev[kind].pages + 1, loadingMore: false } }));
    } catch (error) {
      if (!isAbortError(error) && generation.current === current)
        setState((prev) => ({ ...prev, [kind]: { ...prev[kind], loadingMore: false, moreError: true } }));
    } finally { if (generation.current === current) inFlight.current.delete(kind); }
  }, [state, owner, integrationId]);
  return { ...(state.owner === owner && state.scope === integrationId ? state : initial(owner, integrationId)), refreshSequence: reload, retry, loadMore };
}
