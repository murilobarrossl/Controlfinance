import { useState } from "react";

export default function useSaving() {
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  async function run(action) {
    setSaving(true);
    setError(null);
    try {
      await action();
      return true;
    } catch (err) {
      setError(err?.message || "Não foi possível salvar. Tente de novo.");
      return false;
    } finally {
      setSaving(false);
    }
  }

  return { saving, error, setError, run };
}
