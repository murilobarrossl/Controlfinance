import "./StatusPill.css";

const STATUS_CLASS = {
  Paid: "status-pill--paid",
  Pending: "status-pill--pending",
  Overdue: "status-pill--overdue",
};

export default function StatusPill({ status, children }) {
  const classes = ["status-pill", STATUS_CLASS[status] || "status-pill--pending"].join(" ");
  return <span className={classes}>{children}</span>;
}
