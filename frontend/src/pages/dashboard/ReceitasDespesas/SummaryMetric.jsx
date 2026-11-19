import { TrendUpIcon, TrendDownIcon } from "../../../components/ui/icons/FeatureIcons.jsx";

export default function SummaryMetric({ label, value, tone, trend }) {
  return (
    <dl className="rd-summary">
      <dt className="rd-summary__label">{label}</dt>
      <dd className={`rd-summary__value rd-summary__value--${tone}`}>{value}</dd>
      {trend && (
        <dd className={`rd-summary__trend rd-summary__trend--${trend.tone}`}>
          <span aria-hidden="true">{trend.direction === "up" ? <TrendUpIcon /> : <TrendDownIcon />}</span>
          {trend.text}
        </dd>
      )}
    </dl>
  );
}
