import "./Card.css";

export default function Card({ title, action, children, className = "" }) {
  const classes = ["card", className].filter(Boolean).join(" ");

  return (
    <div className={classes}>
      {(title || action) && (
        <div className="card__header">
          {title && <h3 className="card__title">{title}</h3>}
          {action}
        </div>
      )}
      {children}
    </div>
  );
}
