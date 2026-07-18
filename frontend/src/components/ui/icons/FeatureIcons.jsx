const BASE_PROPS = {
  width: 24,
  height: 24,
  viewBox: "0 0 24 24",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: 1.6,
  strokeLinecap: "round",
  strokeLinejoin: "round",
};

export function WalletIcon() {
  return (
    <svg {...BASE_PROPS}>
      <path d="M3 7a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v1H3V7z" />
      <rect x="3" y="8" width="18" height="11" rx="2" />
      <circle cx="17" cy="13.5" r="1.1" fill="currentColor" stroke="none" />
    </svg>
  );
}

export function SyncIcon() {
  return (
    <svg {...BASE_PROPS}>
      <path d="M4 12a8 8 0 0 1 14-5.3M20 4v4h-4" />
      <path d="M20 12a8 8 0 0 1-14 5.3M4 20v-4h4" />
    </svg>
  );
}

export function SparkleIcon() {
  return (
    <svg {...BASE_PROPS}>
      <path
        d="M12 2 L14 10 L22 12 L14 14 L12 22 L10 14 L2 12 L10 10 Z"
        strokeLinejoin="round"
      />
    </svg>
  );
}

export function TargetIcon() {
  return (
    <svg {...BASE_PROPS}>
      <circle cx="12" cy="12" r="9" />
      <circle cx="12" cy="12" r="5" />
      <circle cx="12" cy="12" r="1.2" fill="currentColor" stroke="none" />
    </svg>
  );
}

export function ChartIcon() {
  return (
    <svg {...BASE_PROPS} strokeWidth={2.4}>
      <path d="M4 20V10M12 20V4M20 20v-7" />
    </svg>
  );
}

export function BuildingIcon() {
  return (
    <svg {...BASE_PROPS}>
      <rect x="4" y="3" width="10" height="18" rx="1" />
      <rect x="14" y="9" width="6" height="12" rx="1" />
      <path d="M7 7h1M11 7h1M7 11h1M11 11h1M7 15h1M11 15h1" />
    </svg>
  );
}

export function CardIcon() {
  return (
    <svg {...BASE_PROPS}>
      <rect x="3" y="5" width="18" height="14" rx="2" />
      <path d="M3 9h18" />
      <path d="M6 14h4M6 16.5h6" />
    </svg>
  );
}

export function BellIcon() {
  return (
    <svg {...BASE_PROPS}>
      <path d="M6 10a6 6 0 0 1 12 0c0 4 1.5 5.5 1.5 5.5H4.5S6 14 6 10z" />
      <path d="M9.5 18.5a2.5 2.5 0 0 0 5 0" />
    </svg>
  );
}

export function ExportIcon() {
  return (
    <svg {...BASE_PROPS}>
      <path d="M12 3v12" />
      <path d="M7 10l5 5 5-5" />
      <path d="M4 19h16" />
    </svg>
  );
}

export function HistoryIcon() {
  return (
    <svg {...BASE_PROPS}>
      <circle cx="12" cy="13" r="8" />
      <path d="M12 9v4l3 2" />
      <path d="M8 2h8" />
    </svg>
  );
}

export function TrendUpIcon() {
  return (
    <svg {...BASE_PROPS}>
      <path d="M3 17l6-6 4 4 8-8" />
      <path d="M15 7h6v6" />
    </svg>
  );
}

export function TrendDownIcon() {
  return (
    <svg {...BASE_PROPS}>
      <path d="M3 7l6 6 4-4 8 8" />
      <path d="M15 17h6v-6" />
    </svg>
  );
}

export function ShieldIcon() {
  return (
    <svg {...BASE_PROPS}>
      <path d="M12 3l7 3v6c0 5-3.5 8-7 9-3.5-1-7-4-7-9V6l7-3z" />
      <path d="M9 12l2 2 4-4" />
    </svg>
  );
}

export function ChevronDownIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
      <path
        d="M5.70711 9.71069C5.31658 10.1012 5.31658 10.7344 5.70711 11.1249L10.5993 16.0123C11.3805 16.7927 12.6463 16.7924 13.4271 16.0117L18.3174 11.1213C18.708 10.7308 18.708 10.0976 18.3174 9.70708C17.9269 9.31655 17.2937 9.31655 16.9032 9.70708L12.7176 13.8927C12.3271 14.2833 11.6939 14.2832 11.3034 13.8927L7.12132 9.71069C6.7308 9.32016 6.09763 9.32016 5.70711 9.71069Z"
        fill="currentColor"
      />
    </svg>
  );
}

export function MenuIcon() {
  return (
    <svg {...BASE_PROPS}>
      <path d="M4 7h16M4 12h16M4 17h16" />
    </svg>
  );
}

export function CloseIcon() {
  return (
    <svg {...BASE_PROPS}>
      <path d="M6 6l12 12M18 6L6 18" />
    </svg>
  );
}
