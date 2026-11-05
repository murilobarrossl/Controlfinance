import "./SectionHeading.css";

export default function SectionHeading({ title, children }) {
  return (
    <div className="section-heading">
      <h2 className="section-heading__title">{title}</h2>
      {children && <p className="section-heading__lead">{children}</p>}
    </div>
  );
}
