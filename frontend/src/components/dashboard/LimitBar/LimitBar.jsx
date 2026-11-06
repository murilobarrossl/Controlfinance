import "./LimitBar.css";

export default function LimitBar({ usagePercent, size = "md" }) {
  if (usagePercent == null) return null;
  const over = usagePercent > 100;
  const withinWidth = over ? (100 / usagePercent) * 100 : usagePercent;

  return (
    <span className={`limit-bar limit-bar--${size}`} aria-hidden="true">
      <span className="limit-bar__fill" style={{ width: `${withinWidth}%` }} />
      {over && <span className="limit-bar__over" style={{ width: `${100 - withinWidth}%` }} />}
    </span>
  );
}
