function base64UrlDecode(segment) {
  let base64 = segment.replace(/-/g, "+").replace(/_/g, "/");
  while (base64.length % 4) base64 += "=";
  return atob(base64);
}

// Extrai o id do usuário (claim "sub") direto do JWT, sem precisar de chamada ao backend.
export function getCurrentUserId() {
  const token = localStorage.getItem("token");
  if (!token) return null;

  try {
    const payload = JSON.parse(base64UrlDecode(token.split(".")[1]));
    return payload.sub ?? null;
  } catch {
    return null;
  }
}
