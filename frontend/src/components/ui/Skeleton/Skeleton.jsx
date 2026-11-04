import "./Skeleton.css";

export default function Skeleton({ width = "6rem", height = "1em", className = "", style }) {
  return (
    <span
      className={["skeleton", className].filter(Boolean).join(" ")}
      style={{ width, height, ...style }}
      aria-hidden="true"
    />
  );
}
