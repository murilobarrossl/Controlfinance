import "./StatusPill.css";

const STATUS_CLASS = {
  Paid: "status-pill--paid",
  Pending: "status-pill--pending",
  Overdue: "status-pill--overdue",
  Neutral: "status-pill--neutral",
};

export default function StatusPill({ status, children, pendingTone = "warning" }) {
  const statusClass = status === "Pending" && pendingTone === "neutral"
    ? "status-pill--neutral"
    : STATUS_CLASS[status] || "status-pill--neutral";
  const classes = ["status-pill", statusClass].join(" ");
  return <span className={classes}>{children}</span>;
}
