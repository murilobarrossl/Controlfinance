import { useEffect, useRef, useState } from "react";
import { applyMask, DATE_MASK } from "../../../utils/masks.js";
import { brToIso, isoToBr } from "../../../utils/dates.js";

// Campo de data do app: digitado com máscara dd/mm/aaaa, do mesmo jeito que CPF/CNPJ no login e no
// cadastro (texto, teclado numérico, formato visível), em vez do seletor nativo, que muda de cara e
// de ordem dia/mês conforme o navegador. value/onChange usam "AAAA-MM-DD"; enquanto a data está
// incompleta ou não existe, onChange recebe "". Data inválida ou fora de min/max bloqueia o envio do
// formulário pela validação do próprio navegador, com a mensagem em português.
export default function DateInput({ value = "", onChange, min, max, required = false, placeholder = "dd/mm/aaaa", ...rest }) {
  const inputRef = useRef(null);
  const [text, setText] = useState(() => isoToBr(value));
  const [syncedValue, setSyncedValue] = useState(value);

  // Valor trocado por fora (ex.: o formulário volta para "hoje"): o texto acompanha. Enquanto a
  // pessoa digita, o valor externo é o próprio texto convertido e nada muda.
  if (value !== syncedValue) {
    setSyncedValue(value);
    if ((brToIso(text) ?? "") !== value) setText(isoToBr(value));
  }

  const digits = text.replace(/\D/g, "");
  const iso = brToIso(text);
  let problem = "";
  if (digits.length > 0 && digits.length < 8) problem = "Complete a data no formato dd/mm/aaaa.";
  else if (digits.length === 8 && !iso) problem = "Essa data não existe.";
  else if (iso && min && iso < min) problem = `A data precisa ser a partir de ${isoToBr(min)}.`;
  else if (iso && max && iso > max) problem = `A data precisa ser até ${isoToBr(max)}.`;

  useEffect(() => {
    inputRef.current?.setCustomValidity(problem);
  }, [problem]);

  function handleChange(e) {
    const nextDigits = e.target.value.replace(/\D/g, "").slice(0, 8);
    const next = applyMask(nextDigits, DATE_MASK, digits);
    setText(next);
    const nextIso = brToIso(next) ?? "";
    setSyncedValue(nextIso);
    onChange(nextIso);
  }

  return (
    <input
      ref={inputRef}
      type="text"
      inputMode="numeric"
      autoComplete="off"
      maxLength={DATE_MASK.length}
      placeholder={placeholder}
      value={text}
      onChange={handleChange}
      required={required}
      aria-invalid={problem ? true : undefined}
      {...rest}
    />
  );
}
