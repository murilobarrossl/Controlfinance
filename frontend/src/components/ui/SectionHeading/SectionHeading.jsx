import "./SectionHeading.css";

export default function SectionHeading({ title, children, level = 2 }) {
  const Heading = level === 1 ? "h1" : "h2";
  return (
    <div className="section-heading">
      <Heading className="section-heading__title">{title}</Heading>
      {children && <p className="section-heading__lead">{children}</p>}
    </div>
  );
}
