# Avaliação de legítimo interesse — Control Finance

Registro exigido pela LGPD (art. 10, §§ 2º e 3º, e art. 37) para os tratamentos que a Política de Privacidade
(seção 3) apoia no legítimo interesse (art. 7º, IX). O método segue as três fases do
[Guia Orientativo da ANPD sobre Legítimo Interesse](https://www.gov.br/anpd/pt-br/centrais-de-conteudo/materiais-educativos-e-publicacoes/guia_orientativo_hipoteses_legais_tratamento_de_dados_pessoais_legitimo_interesse)
(v1.0, fevereiro de 2024): finalidade, necessidade, balanceamento e salvaguardas. O modelo do Anexo II do guia é
facultativo.

- **Data:** 18/11/2026 (versão 1).
- **Preenchido por:** equipe de desenvolvimento. Não houve revisão jurídica externa.
- **Situação:** avaliação preliminar. Ela documenta o raciocínio e as salvaguardas que existem no código, mas não
  valida juridicamente os tratamentos. Revisar quando o controlador estiver constituído, quando um tratamento mudar e,
  no mínimo, uma vez por ano.

Fora deste registro: dados de funcionários e fornecedores cadastrados por empresas, em que o Control Finance é
operador e a base legal é definida pela empresa controladora (Termos de Uso, seção 5).

---

## 1. Proteção da conta contra tentativas abusivas

| Campo | Conteúdo |
|---|---|
| Operação | Limitar tentativas de login, cadastro e exclusão de conta (`RateLimitService`) |
| Dados | IP já convertido em código irreversível e o identificador digitado (e-mail ou CPF/CNPJ), só em memória |
| Titulares | Usuários e qualquer pessoa que tente acessar uma conta |
| Finalidade | Impedir tentativas automatizadas contra uma conta ou distribuídas entre contas |
| Hipótese | Legítimo interesse do controlador (art. 7º, IX; art. 10, II: proteção do titular) |

**Fase 1, finalidade.** É um interesse concreto e atual: endpoints de autenticação expostos na internet recebem
tentativas automatizadas. O interesse é lícito e também protege o próprio titular.

**Fase 2, necessidade.** Sem o IP não há como limitar ataques distribuídos, e sem o identificador não há como
proteger uma conta específica. A minimização já existe: o IP não é gravado, fica só em memória por até 15 minutos e já
convertido. Nenhuma alternativa menos intrusiva atende a mesma finalidade.

**Fase 3, balanceamento e salvaguardas.** Quem acessa um serviço on-line espera esse tipo de controle. O impacto é
baixo: um bloqueio temporário, sem decisão sobre a pessoa e sem perfil. As salvaguardas são retenção curta, nenhum
armazenamento persistente e transparência na Política (seções 2 e 3).

**Conclusão preliminar:** o balanceamento é favorável. Não há risco que indique a necessidade de RIPD.

---

## 2. Dados de terceiros que aparecem nas transações importadas

| Campo | Conteúdo |
|---|---|
| Operação | Guardar e mostrar ao usuário os dados da outra parte de cada transação |
| Dados | Nome comercial, razão social e CNPJ (identificados pela Polp), tipo de pessoa, código do banco e 4 últimos dígitos da conta. CPF e agência chegam da Polp e são descartados |
| Titulares | Pessoas físicas e representantes de empresas que pagam ou recebem do usuário |
| Finalidade | Mostrar ao próprio usuário o extrato dele e os detalhes de cada transação |
| Hipótese | Legítimo interesse do controlador e do usuário (art. 7º, IX) |

**Fase 1, finalidade.** Quem contratou o serviço precisa saber com quem transacionou. A informação já está no
extrato que o banco fornece ao próprio usuário.

**Fase 2, necessidade.** Sem a outra parte, o extrato perde utilidade. A minimização já existe: o CPF e a agência não
são guardados, a conta fica só com 4 dígitos e tudo é cifrado em repouso. Nada é usado para contato, perfil ou
publicidade.

**Fase 3, balanceamento e salvaguardas.** O terceiro tende a esperar que seu nome apareça no extrato de quem pagou ou
recebeu. O impacto é baixo, porque só o titular da conta vê os dados, e o isolamento por usuário é testado. As
salvaguardas são cifragem, isolamento, retenção ligada à conexão (desconexão, fim da autorização ou prazo do histórico)
e transparência na Política.

**Lacuna:** não existe um meio próprio para o terceiro se opor, além do canal de privacidade.

**Conclusão preliminar:** o balanceamento é favorável.

---

## 3. Códigos derivados do CPF e da conta do destinatário (só contas CNPJ)

| Campo | Conteúdo |
|---|---|
| Operação | Calcular e guardar HMAC-SHA256 do CPF e da conta completa do destinatário de cada transação, com chave secreta e o id da empresa (`EmployeeKeys.CpfLookupInput`, `Transaction.CounterpartyCpfHash`/`CounterpartyAccountHash`) |
| Dados | CPF e conta completa do destinatário, enviados pelo banco (só os códigos são guardados) |
| Titulares | Destinatários de pagamentos da empresa: funcionários e qualquer outra pessoa física que receba dela |
| Finalidade | Identificar, para a empresa, pagamentos aos funcionários que ela mesma cadastrou (`SupplierPaymentMatcher.MatchEmployee`) |
| Hipótese | Legítimo interesse do controlador e da empresa usuária (art. 7º, IX) |

**Fase 1, finalidade.** A empresa quer saber quais funcionários já receberam no mês sem conferir o extrato
manualmente. É um interesse concreto e lícito, ligado à gestão da folha.

**Fase 2, necessidade.** O CPF e a conta são os únicos identificadores exatos que o banco envia (a Polp v2 não envia a
chave Pix). Usar nomes seria menos preciso e mais arriscado. A minimização já existe: o dado em claro não é guardado; o
código é calculado por empresa, então não serve para cruzar dados entre empresas; o recurso existe só para CNPJ; e
nome, descrição e classificação da Polp nunca entram na comparação.

**Fase 3, balanceamento e salvaguardas.**
- **Pontos de atenção:**
  - o código é calculado para todo destinatário pessoa física de uma saída, não só para funcionários (desde
    19/11/2026 nunca para entradas, isto é, para quem paga a empresa);
  - o CPF tem poucas combinações possíveis: com a chave, é possível testar CPFs, então isto é pseudonimização, não
    anonimização;
  - o tratamento pode ser em grande escala (milhares de empresas).
- **Salvaguardas:**
  - chave secreta fora do banco de dados, com rotação prevista (`Encryption:LookupKeys`);
  - código por empresa;
  - exibição só para a empresa;
  - quando há mais de um candidato, nenhum funcionário é identificado;
  - o resultado é informado como "identificado", sem afirmar quitação;
  - a marcação manual da empresa prevalece;
  - retenção junto da transação;
  - transparência na Política (seções 2 e 3).
- **Expectativa do titular:** um funcionário tende a esperar que o empregador confira os pagamentos feitos a ele. Um
  terceiro sem relação de emprego não tem essa expectativa, mas para ele o código fica sem uso, porque não corresponde a
  nenhum cadastro.

**Conclusão preliminar:** o balanceamento parece favorável com as salvaguardas atuais. Duas pendências pedem decisão:

- (a) avaliar se o código deve ser calculado só quando a empresa tem funcionários cadastrados, reduzindo o tratamento
  de terceiros sem relação de emprego;
- (b) elaborar um Relatório de Impacto (RIPD, art. 38), que a ANPD pode exigir para tratamento baseado em legítimo
  interesse (art. 10, § 3º), por causa da escala e do uso de CPF de terceiros.

Nenhuma das duas mudanças foi implementada nesta etapa.
