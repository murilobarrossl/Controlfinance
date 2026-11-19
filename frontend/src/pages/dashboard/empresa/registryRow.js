// A linha inteira abre o cadastro; o nome continua sendo o link (teclado, leitor de tela e
// clique do meio). Clique em outro controle ou seleção de texto não navega.
export function openRow(event, navigate, to) {
  if (event.target.closest("a, button, input, select, textarea, label")) return;
  if (window.getSelection()?.toString()) return;
  navigate(to);
}
