import { useEffect, useState } from "react";

export default function useIsNarrow(maxWidth = 480) {
  const [element, setElement] = useState(null);
  const [narrow, setNarrow] = useState(false);

  useEffect(() => {
    if (!element || typeof ResizeObserver === "undefined") return undefined;
    const observer = new ResizeObserver(([entry]) => setNarrow(entry.contentRect.width < maxWidth));
    observer.observe(element);
    return () => observer.disconnect();
  }, [element, maxWidth]);

  return [setElement, narrow];
}
