import { useState } from "react";

export default function useDialogSubmit(action, onSaved) {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");
  async function submit(e) {
    e.preventDefault();
    setError("");
    setSubmitting(true);
    try {
      onSaved(await action());
    } catch (err) {
      setError(err.message || "Não foi possível salvar.");
      setSubmitting(false);
    }
  }
  return { submitting, error, setError, submit };
}
