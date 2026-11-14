import { useEffect, useMemo, useState } from "react";
import { investmentDetail, isThrottled } from "../../../api/investments.js";
import { normalizeInvestment } from "../../../utils/importedInvestments.js";

const refsOf = (owner) => [
  ...(owner?.investmentIds ?? []).map((id) => ({ id, reserved: false, key: `investment:${id}` })),
  ...(owner?.reservedBalanceIds ?? []).map((id) => ({ id, reserved: true, key: `reserved:${id}` })),
];

// A lista de investimentos é paginada: um item vinculado à reserva ou a uma meta pode não estar nas
// páginas carregadas. Busca o detalhe só desses, para a soma nunca sair parcial sem avisar.
export default function useLinkedResources(planning, items, connections, refreshKey) {
  const refs = useMemo(() => [planning.reserve, ...planning.goals].flatMap(refsOf), [planning.reserve, planning.goals]);
  const loaded = useMemo(() => new Map(items.map((item) => [item.key, item])), [items]);
  const missing = refs.filter((ref) => !loaded.has(ref.key));
  const missingKey = missing.map((ref) => ref.key).sort().join("|");
  const [fetched, setFetched] = useState({});

  useEffect(() => {
    if (!missingKey) return;
    const controller = new AbortController();
    for (const ref of missingKey.split("|").map((key) => ({ key, reserved: key.startsWith("reserved:"), id: key.slice(key.indexOf(":") + 1) }))) {
      investmentDetail(ref.id, controller.signal, ref.reserved)
        .then((data) => { if (!controller.signal.aborted) setFetched((prev) => ({ ...prev, [ref.key]: { refreshKey, data } })); })
        .catch((error) => {
          if (controller.signal.aborted) return;
          // Recusa por limite não quer dizer que o investimento sumiu: mantém o que já estava na tela.
          setFetched((prev) => ({ ...prev, [ref.key]: isThrottled(error) && prev[ref.key]?.data ? { ...prev[ref.key], refreshKey }
            : { refreshKey, data: null, throttled: isThrottled(error) } }));
        });
    }
    return () => controller.abort();
  }, [missingKey, refreshKey]);

  return useMemo(() => (owner) => refsOf(owner).map((ref) => {
    const item = loaded.get(ref.key);
    if (item) return { ...ref, status: "ready", item };
    const entry = fetched[ref.key];
    if (!entry || entry.refreshKey !== refreshKey) return { ...ref, status: "loading" };
    if (entry.data) return { ...ref, status: "ready", item: normalizeInvestment(entry.data, ref.reserved, connections) };
    return { ...ref, status: entry.throttled ? "error" : "missing" };
  }), [loaded, fetched, refreshKey, connections]);
}
