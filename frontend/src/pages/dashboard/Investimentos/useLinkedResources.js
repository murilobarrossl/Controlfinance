import { useEffect, useMemo, useState } from "react";
import { linkedResources } from "../../../api/investments.js";
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
  const missingKey = [...new Set(missing.map((ref) => ref.key))].sort().join("|");
  const [fetched, setFetched] = useState({});

  useEffect(() => {
    if (!missingKey) return;
    const controller = new AbortController();
    async function load() {
      const keys = missingKey.split("|");
      for (let i = 0; i < keys.length && !controller.signal.aborted; i += 100) {
        const batch = keys.slice(i, i + 100);
        const ids = (kind) => batch.filter((key) => key.startsWith(kind + ":")).map((key) => key.split(":")[1]);
        try {
          const data = await linkedResources(ids("investment"), ids("reserved"), controller.signal);
          const rows = new Map([
            ...data.investments.map((row) => ["investment:" + row.id, row]),
            ...data.reserves.map((row) => ["reserved:" + row.id, row]),
          ]);
          if (!controller.signal.aborted) setFetched((prev) => ({ ...prev, ...Object.fromEntries(
            batch.map((key) => [key, { refreshKey, data: rows.get(key) ?? null }])) }));
        } catch {
          if (controller.signal.aborted) return;
          setFetched((prev) => ({ ...prev, ...Object.fromEntries(batch.map((key) => [key,
            { refreshKey, data: null, failed: true }])) }));
        }
      }
    }
    load();
    return () => controller.abort();
  }, [missingKey, refreshKey]);

  return useMemo(() => (owner) => refsOf(owner).map((ref) => {
    const item = loaded.get(ref.key);
    if (item) return { ...ref, status: "ready", item };
    const entry = fetched[ref.key];
    if (!entry || entry.refreshKey !== refreshKey) return { ...ref, status: "loading" };
    if (entry.data) return { ...ref, status: "ready", item: normalizeInvestment(entry.data, ref.reserved, connections) };
    return { ...ref, status: entry.failed ? "error" : "missing" };
  }), [loaded, fetched, refreshKey, connections]);
}
