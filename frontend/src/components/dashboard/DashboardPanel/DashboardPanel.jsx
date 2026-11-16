import "./DashboardPanel.css";

export default function DashboardPanel({ title, action, children, className = "" }) {
  return (
    <section className={`dashboard-panel ${className}`}>
      {(title || action) && (
        <header className="dashboard-panel__header">
          {title && <h2 className="dashboard-panel__title">{title}</h2>}
          {action}
        </header>
      )}
      {children}
    </section>
  );
}
