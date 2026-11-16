import { TrendUpIcon, TrendDownIcon } from "../../ui/icons/FeatureIcons.jsx";
import "./DashboardMetric.css";

export default function DashboardMetric({ label, value, valueTone = "default", secondaryLines = [], trend }) {
  return (
    <dl className="dashboard-metric">
      <dt className="dashboard-metric__label">{label}</dt>
      <dd className={`dashboard-metric__value dashboard-metric__value--${valueTone}`}>{value}</dd>
      {trend && (
        <dd className={`dashboard-metric__trend dashboard-metric__trend--${trend.tone}`}>
          <span aria-hidden="true">{trend.direction === "up" ? <TrendUpIcon /> : <TrendDownIcon />}</span>
          {trend.text}
        </dd>
      )}
      {secondaryLines.map((line, index) => (
        <dd className="dashboard-metric__note" key={index}>
          <span>{line.label}</span> <span className={line.tone ? `dashboard-metric__note-value--${line.tone}` : undefined}>{line.value}</span>
        </dd>
      ))}
    </dl>
  );
}
