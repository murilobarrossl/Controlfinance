import { formatPercentage } from "./financeMath.js";
import { poupancaAnnualPct } from "./investmentMath.js";

// O legado baseado em contas não contém o contrato do investimento. A regra manual sai de:
// regra legal (poupança, a partir da Selic meta), indexador oficial (Tesouro Selic, Selic efetiva)
// ou da taxa contratada que o próprio usuário informa (caixinha, CDB, LCI...). Sem uma delas, não
// há projeção: nenhuma taxa genérica entra no lugar.

export const RATE_KINDS = {
  CdiPercent: {
    key: "CdiPercent",
    label: "Percentual do CDI",
    fieldLabel: "Quantos % do CDI",
    placeholder: "Ex: 100",
    help: "Aparece no app do banco como “rende X% do CDI”.",
    min: 0.01,
    max: 300,
  },
  Prefixed: {
    key: "Prefixed",
    label: "Prefixado",
    fieldLabel: "Taxa ao ano (%)",
    placeholder: "Ex: 12,5",
    help: "Taxa fixa contratada, em % ao ano.",
    min: 0.01,
    max: 100,
  },
  SelicSpread: {
    key: "SelicSpread",
    label: "Selic + adicional",
    fieldLabel: "Adicional sobre a Selic (% ao ano)",
    placeholder: "Ex: 0,05",
    help: "No Tesouro Selic é a taxa mostrada na compra do título. Use 0 se não houver adicional.",
    min: -5,
    max: 10,
  },
};

export function allowedRateKinds(modalityKey) {
  if (modalityKey === "poupanca") return [];
  if (modalityKey === "tesouroSelic") return ["SelicSpread"];
  return ["CdiPercent", "Prefixed", "SelicSpread"];
}

function combine(baseAnnualPct, spreadPct) {
  return ((1 + baseAnnualPct / 100) * (1 + spreadPct / 100) - 1) * 100;
}

function formatSpread(value) {
  if (!value) return "Selic";
  return `Selic ${value > 0 ? "+" : "−"} ${formatPercentage(Math.abs(value))} ao ano`;
}

export function describeProfile(profile) {
  if (!profile) return null;
  if (profile.kind === "CdiPercent") return `${formatPercentage(profile.value)} do CDI`;
  if (profile.kind === "Prefixed") return `${formatPercentage(profile.value)} ao ano, prefixado`;
  if (profile.kind === "SelicSpread") return formatSpread(profile.value);
  return null;
}

function poupancaRuleLabel(selicPct) {
  return selicPct > 8.5 ? "0,5% ao mês + TR" : "70% da Selic meta + TR";
}

function officialBasis(name, rate) {
  return { name, annualPct: rate.annualPercent, date: rate.date };
}

// status:
//  "ready"        → annualPct pronto pra projetar
//  "needsRate"    → o usuário ainda não informou a regra dessa aplicação
//  "unavailable"  → a regra depende de uma taxa oficial que não veio do Banco Central agora
export function resolveImportedYieldRule(investment) {
  // Imported monetary/rate values are exact decimal strings. They must never pass through
  // Number/Math.pow or inherit a manual account profile / a generic Selic fallback.
  const capability = investment?.projection;
  return {
    status: "unsupported",
    source: "imported_contract",
    reason: capability?.reason ?? "imported_projection_evidence_not_available",
    indexer: capability?.indexer ?? null,
    contractualValues: capability?.contractualValues ?? investment?.contract?.values ?? null,
    message: "Projeção indisponível: as unidades da taxa e as convenções de cálculo deste contrato precisam estar confirmadas.",
  };
}

export function resolveYieldRule({ modalityKey, profile, rates, isCompany, importedInvestment }) {
  if (importedInvestment != null) return resolveImportedYieldRule(importedInvestment);
  if (modalityKey === "poupanca") {
    const selic = rates?.selicTarget;
    if (!selic) return { status: "unavailable", indexer: "Selic meta" };
    return {
      status: "ready",
      source: "legal",
      label: poupancaRuleLabel(selic.annualPercent),
      annualPct: poupancaAnnualPct(selic.annualPercent),
      taxed: false,
      beforeTax: isCompany,
      basis: officialBasis("Selic meta", selic),
      excludesTr: true,
    };
  }

  const effectiveProfile =
    profile ?? (modalityKey === "tesouroSelic" ? { kind: "SelicSpread", value: 0, incomeTaxExempt: false, implicit: true } : null);
  if (!effectiveProfile) return { status: "needsRate" };

  const taxed = !effectiveProfile.incomeTaxExempt;
  const base = { source: effectiveProfile.implicit ? "indexer" : "user", taxed, beforeTax: false, profile: effectiveProfile };

  if (effectiveProfile.kind === "Prefixed") {
    return { ...base, status: "ready", label: describeProfile(effectiveProfile), annualPct: effectiveProfile.value, basis: null };
  }

  if (effectiveProfile.kind === "CdiPercent") {
    const cdi = rates?.cdi;
    if (!cdi) return { status: "unavailable", indexer: "CDI" };
    return {
      ...base,
      status: "ready",
      label: describeProfile(effectiveProfile),
      annualPct: (cdi.annualPercent * effectiveProfile.value) / 100,
      basis: officialBasis("CDI", cdi),
    };
  }

  if (effectiveProfile.kind === "SelicSpread") {
    const selic = rates?.selicEffective;
    if (!selic) return { status: "unavailable", indexer: "Selic" };
    return {
      ...base,
      status: "ready",
      label: describeProfile(effectiveProfile),
      annualPct: combine(selic.annualPercent, effectiveProfile.value),
      basis: officialBasis("Selic efetiva", selic),
      spreadMissing: Boolean(effectiveProfile.implicit),
    };
  }

  return { status: "needsRate" };
}

export function shortRuleLabel(modalityKey, profile) {
  if (modalityKey === "poupanca") return "Regra da poupança";
  if (profile) return describeProfile(profile);
  if (modalityKey === "tesouroSelic") return "Selic";
  return "Taxa não informada";
}

export function formatRateDate(iso) {
  if (!iso) return "";
  const [year, month, day] = iso.split("-");
  return `${day}/${month}/${year}`;
}
