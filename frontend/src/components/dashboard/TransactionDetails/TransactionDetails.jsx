import { useEffect, useState } from "react";
import { getTransactionDetails } from "../../../api/transactions.js";
import { isAbortError } from "../../../api/client.js";
import "../../ui/Pager/Pager.css";
import Skeleton from "../../ui/Skeleton/Skeleton.jsx";

export default function TransactionDetails({ query, className, renderItem }) {
  const queryKey = JSON.stringify(query);
  return <DetailPage key={queryKey} queryKey={queryKey} className={className} renderItem={renderItem} />;
}

function DetailPage({ queryKey, className, renderItem }) {
  const [cursors, setCursors] = useState([null]);
  const cursor = cursors.at(-1);
  const [result, setResult] = useState(null);

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;

    getTransactionDetails({ ...JSON.parse(queryKey), cursor }, { signal })
      .then((page) => {
        if (!signal.aborted) setResult({ cursor, ...page, error: "" });
      })
      .catch((err) => {
        if (signal.aborted || isAbortError(err)) return;
        setResult({ cursor, items: [], error: err.message || "Não foi possível carregar os lançamentos." });
      });

    return () => controller.abort();
  }, [queryKey, cursor]);

  if (!result || result.cursor !== cursor) return <Skeleton width="100%" height="3rem" />;

  return <>
    {result.error ? <p className={className}>{result.error}</p> : <ul className={className}>{result.items.map(renderItem)}</ul>}
    {(cursors.length > 1 || result.nextCursor) && (
      <nav className="pager" aria-label="Páginas dos lançamentos">
        <button type="button" aria-label="Página anterior" disabled={cursors.length === 1} onClick={() => setCursors(cursors.slice(0, -1))}>‹</button>
        <span>Página {cursors.length}</span>
        <button type="button" aria-label="Próxima página" disabled={!result.nextCursor} onClick={() => setCursors([...cursors, result.nextCursor])}>›</button>
      </nav>
    )}
  </>;
}
