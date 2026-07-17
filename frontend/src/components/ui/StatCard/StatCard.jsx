import Card from "../Card/Card.jsx";
import { TrendUpIcon, TrendDownIcon } from "../icons/FeatureIcons.jsx";
import "./StatCard.css";

export default function StatCard({ icon, label, value, valueTone = "default", secondaryLines = [], trend }) {
  return (
    <Card className="stat-card">
      <div className="stat-card__header">
        <span className={`stat-card__icon stat-card__icon--${valueTone}`}>{icon}</span>
        <span className="stat-card__label">{label}</span>
      </div>

      <div className="stat-card__value-row">
        <span className={`stat-card__value stat-card__value--${valueTone}`}>{value}</span>
        {trend && (
          <span className={`stat-card__trend stat-card__trend--${trend.tone || (trend.direction === "up" ? "positive" : "negative")}`}>
            {trend.direction === "up" ? <TrendUpIcon /> : <TrendDownIcon />}
            {trend.text}
          </span>
        )}
      </div>

      {secondaryLines.length > 0 && (
        <div className="stat-card__secondary">
          {secondaryLines.map((line) => (
            <div key={line.label} className="stat-card__secondary-line">
              <span>{line.label}</span>
              <span>{line.value}</span>
            </div>
          ))}
        </div>
      )}
    </Card>
  );
}
