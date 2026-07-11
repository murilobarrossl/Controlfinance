import "./SectionHeading.css";

export default function SectionHeading({ kicker, title, align = "center" }) {
  return (
    <div className={`section-heading section-heading--${align}`}>
      {kicker && <span className="section-heading__kicker">{kicker}</span>}
      <h2 className="section-heading__title">{title}</h2>
      <span className="section-heading__bar" />
    </div>
  );
}
