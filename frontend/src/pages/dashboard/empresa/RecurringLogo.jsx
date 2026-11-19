import { recurringLogo } from "./recurringLogo.js";

const GLYPHS = {
  fitness: "M6 7v10M18 7v10M3 10v4M21 10v4M6 12h12",
  food: "M3 18h18M5 15h14M5 15a7 7 0 0 1 14 0M12 8V6",
  fuel: "M3 20h12M5 20V5a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v15M8 8h2M13 10h2a2 2 0 0 1 2 2v4a1.5 1.5 0 0 0 3 0V8l-2.5-2.5",
  media: "M5 5h14a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2zM10 9.5v5l4.5-2.5z",
  shopping: "M6 8h12l-1 12H7zM9 8V7a3 3 0 0 1 6 0v1",
  transport: "M5 17a1 1 0 0 1-1-1v-3.5l2-5A1 1 0 0 1 7 7h10a1 1 0 0 1 1 .5l2 5V16a1 1 0 0 1-1 1zM4 12.5h16M7 17v2M17 17v2",
  digital: "M8.5 7h7A1.5 1.5 0 0 1 17 8.5v7a1.5 1.5 0 0 1-1.5 1.5h-7A1.5 1.5 0 0 1 7 15.5v-7A1.5 1.5 0 0 1 8.5 7zM10 3v4M14 3v4M10 17v4M14 17v4M3 10h4M3 14h4M17 10h4M17 14h4",
  neutral: "M6 3h12v18l-3-2-3 2-3-2-3 2zM9 8h6M9 12h6",
};

export default function RecurringLogo({ name }) {
  const match = recurringLogo(name);
  const brand = match?.brand;
  if (!brand) {
    return (
      <span className={`recurring-logo recurring-logo--glyph${match ? "" : " recurring-logo--neutral"}`} aria-hidden="true">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d={GLYPHS[match?.category] ?? GLYPHS.neutral} />
        </svg>
      </span>
    );
  }
  return (
    <span className={`recurring-logo${brand.wide ? " recurring-logo--wide" : ""}`} style={brand.color ? { color: brand.color } : undefined}
      role="img" aria-label={brand.title} title={brand.title}>
      <svg viewBox={brand.viewBox ?? "0 0 24 24"} fill="currentColor" aria-hidden="true">
        <g transform={brand.transform}>
          {brand.paths.map((p, i) => <path key={i} d={p.d} fill={p.fill} fillRule={p.fillRule} />)}
        </g>
      </svg>
    </span>
  );
}
