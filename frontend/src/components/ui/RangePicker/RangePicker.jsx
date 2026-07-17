import "./RangePicker.css";

export default function RangePicker({ label, onPrev, onNext, prevDisabled = false, nextDisabled = false }) {
  return (
    <div className="range-picker">
      <button type="button" onClick={onPrev} disabled={prevDisabled} aria-label="Período anterior">
        ‹
      </button>
      <span>{label}</span>
      <button type="button" onClick={onNext} disabled={nextDisabled} aria-label="Próximo período">
        ›
      </button>
    </div>
  );
}
