import "./Avatar.css";

function getInitials(name) {
  if (!name) return "?";
  const parts = name.trim().split(/\s+/);
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase();
}

export default function Avatar({ name, size = "md", className = "" }) {
  const classes = ["avatar", `avatar--${size}`, className].filter(Boolean).join(" ");
  return <span className={classes}>{getInitials(name)}</span>;
}
