import { useEffect, useState } from "react";
import { isAbortError } from "../../../api/client.js";

export function useDebounced(value, delay) {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const id = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(id);
  }, [value, delay]);
  return debounced;
}

export default function useRegistryData({ loadSummary, loadList, filters, refreshKey, pageSize = 25, listError, summaryError: summaryErrorText }) {
  const filterKey = JSON.stringify(filters);
  const [pageState, setPageState] = useState({ filterKey, page: 1 });
  const page = pageState.filterKey === filterKey ? pageState.page : 1;
  const summaryKey = String(refreshKey);
  const listKey = `${filterKey}|${page}|${refreshKey}`;
  const [summaryResult, setSummaryResult] = useState(null);
  const [listResult, setListResult] = useState(null);

  useEffect(() => {
    const controller = new AbortController();
    loadSummary({ signal: controller.signal })
      .then((data) => setSummaryResult({ key: summaryKey, data, error: "" }))
      .catch((err) => {
        if (controller.signal.aborted || isAbortError(err)) return;
        setSummaryResult({ key: summaryKey, data: null, error: err.message || summaryErrorText });
      });
    return () => controller.abort();
  }, [loadSummary, summaryKey, summaryErrorText]);

  useEffect(() => {
    const controller = new AbortController();
    loadList({ ...JSON.parse(filterKey), page, pageSize }, { signal: controller.signal })
      .then((data) => {
        if (controller.signal.aborted) return;
        const lastPage = Math.max(1, Math.ceil((data?.totalCount ?? 0) / pageSize));
        if (page > lastPage) {
          setPageState({ filterKey, page: lastPage });
          return;
        }
        setListResult({ key: listKey, data, error: "" });
      })
      .catch((err) => {
        if (controller.signal.aborted || isAbortError(err)) return;
        setListResult({ key: listKey, data: null, error: err.message || listError });
      });
    return () => controller.abort();
  }, [loadList, filterKey, page, pageSize, listKey, listError]);

  const current = listResult?.key === listKey ? listResult : null;
  const shown = current ?? (listResult?.data ? listResult : null);
  const totalCount = shown?.data?.totalCount ?? null;

  return {
    summary: summaryResult?.key === summaryKey ? summaryResult.data : null,
    summaryError: summaryResult?.key === summaryKey ? summaryResult.error : "",
    page,
    setPage: (next) => setPageState({ filterKey, page: Math.max(1, next) }),
    loaded: Boolean(current),
    listError: current?.error ?? "",
    shown: Boolean(shown),
    refreshing: !current && Boolean(shown),
    items: shown?.data?.items ?? [],
    totalCount,
    totalPages: totalCount == null ? null : Math.max(1, Math.ceil(totalCount / pageSize)),
  };
}
