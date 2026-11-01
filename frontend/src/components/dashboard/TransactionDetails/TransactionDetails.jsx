import { useEffect, useState } from "react";
import { getTransactionDetails } from "../../../api/transactions.js";
import { isAbortError } from "../../../api/client.js";
import Skeleton from "../../ui/Skeleton/Skeleton.jsx";

export default function TransactionDetails({ query, className, renderItem }) {
  const queryKey = JSON.stringify(query);
  const [result, setResult] = useState(null);

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;

    getTransactionDetails(JSON.parse(queryKey), { signal })
      .then((items) => {
        if (!signal.aborted) setResult({ key: queryKey, items, error: "" });
      })
      .catch((err) => {
        if (signal.aborted || isAbortError(err)) return;
        setResult({ key: queryKey, items: [], error: err.message || "Não foi possível carregar os lançamentos." });
      });

    return () => controller.abort();
  }, [queryKey]);

  if (result?.key !== queryKey) return <Skeleton width="100%" height="3rem" />;
  if (result.error) return <p className={className}>{result.error}</p>;

  return <ul className={className}>{result.items.map(renderItem)}</ul>;
}
