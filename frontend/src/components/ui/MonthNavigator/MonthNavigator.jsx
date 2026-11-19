import { ChevronDownIcon } from "../icons/FeatureIcons.jsx";
import { toIsoDate } from "../../../utils/monthLabel.js";
import "./MonthNavigator.css";

const modes = [{ value: "ano", label: "Ano" }, { value: "mes", label: "Mês" }];

const monthName = new Intl.DateTimeFormat("pt-BR", { month: "long", timeZone: "UTC" });

function stepPeriod(period, months) {
  const date = new Date(period.date);
  date.setUTCMonth(date.getUTCMonth() + months);
  if (date.getUTCFullYear() < 1) return null;
  const key = toIsoDate(date).slice(0, 7);
  // Dezembro de um ano passado pode avançar para o ano atual, ainda em andamento.
  if (months === 12 && key > period.max && key.slice(0, 4) === period.max.slice(0, 4)) return period.max;
  return key >= period.min && key <= period.max ? key : null;
}

function PeriodArrows({ period, unit, value, onChange }) {
  const isYear = unit === "ano";
  const step = isYear ? 12 : 1;
  const previous = stepPeriod(period, -step);
  const next = stepPeriod(period, step);
  return (
    <div className="month-navigator__group" role="group" aria-label={isYear ? "Ano" : "Mês"}>
      <button type="button" aria-label={isYear ? "Ano anterior" : "Mês anterior"}
        disabled={!previous} onClick={() => onChange(previous, period.mode)}>
        <ChevronDownIcon />
      </button>
      <span className="month-navigator__value" data-unit={unit} aria-live="polite">{value}</span>
      <button type="button" aria-label={isYear ? "Próximo ano" : "Próximo mês"}
        disabled={!next} onClick={() => onChange(next, period.mode)}>
        <ChevronDownIcon />
      </button>
    </div>
  );
}

export default function MonthNavigator({ period, onChange }) {
  const month = monthName.format(period.date);
  return (
    <div className="month-navigator">
      <div className="month-navigator__modes" role="group" aria-label="Exibir por">
        {modes.map(({ value, label }) => (
          <button key={value} type="button" aria-pressed={period.mode === value}
            onClick={() => { if (value !== period.mode) onChange(period.key, value); }}>
            {label}
          </button>
        ))}
      </div>
      <div className="month-navigator__controls">
        <PeriodArrows period={period} unit="ano" value={period.key.slice(0, 4)} onChange={onChange} />
        {!period.annual && (
          <PeriodArrows period={period} unit="mes" value={month[0].toUpperCase() + month.slice(1)} onChange={onChange} />
        )}
      </div>
    </div>
  );
}
