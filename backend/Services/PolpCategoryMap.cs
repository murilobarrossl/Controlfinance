namespace ControlFinance.API.Services;

// Traduz a taxonomia fixa da Polp (category_ref, ex.: FOOD_AND_DRINK_GROCERIES) para o nome da
// categoria do app. Só o grupo de nível mais alto é usado: as ~150 subcategorias da Polp seriam
// categorias demais para o usuário gerir, e o app cria a categoria sob demanda com o mesmo fluxo
// de qualquer outra (ResolveCategoryIdAsync), então ela pode ser renomeada ou recolorida.
public static class PolpCategoryMap
{
    // Ordem importa: prefixos mais específicos primeiro (INCOME_SALARY antes de INCOME).
    private static readonly (string Prefix, string Name)[] Map =
    [
        ("INCOME_SALARY", "Salário"),
        ("INCOME", "Receitas"),
        ("TRANSFER_", "Transferências"),
        ("LOAN_", "Empréstimos"),
        ("BANK_FEES", "Tarifas"),
        ("ENTERTAINMENT", "Lazer"),
        ("FOOD_AND_DRINK", "Alimentação"),
        ("GENERAL_MERCHANDISE", "Compras"),
        ("HOME_IMPROVEMENT", "Casa"),
        ("MEDICAL", "Saúde"),
        ("PERSONAL_CARE", "Cuidados pessoais"),
        ("GENERAL_SERVICES", "Serviços"),
        ("GOVERNMENT_AND_NON_PROFIT", "Governo e doações"),
        ("TRANSPORTATION", "Transporte"),
        ("TRAVEL", "Viagens"),
        ("RENT_AND_UTILITIES", "Contas e aluguel"),
    ];

    public static string ToCategoryName(string? categoryRef)
    {
        if (string.IsNullOrWhiteSpace(categoryRef)) return "Outros";

        foreach (var (prefix, name) in Map)
            if (categoryRef.StartsWith(prefix, StringComparison.Ordinal)) return name;

        return "Outros";
    }
}
