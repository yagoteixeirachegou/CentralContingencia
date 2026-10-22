using System.Text.RegularExpressions;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CentralContingencia.Infrastructure.Hosting;

public sealed record MigrationPlan(IReadOnlyList<string> Applied, IReadOnlyList<string> Pending, string Target, IReadOnlyList<string> DestructiveFindings)
{
    public bool IsDestructive => DestructiveFindings.Count > 0;
}

/// <summary>
/// Aplicação CONTROLADA de migrations (comando <c>--migrate</c>; a API nunca migra sozinha ao iniciar).
/// Salvaguardas: (1) <c>--plan</c> só lista, sem escrever; (2) aplicar exige <c>CENTRAL_MIGRATE_CONFIRM</c> igual ao id da última migration da imagem,
/// ou seja, alguém leu o plano e nomeou o alvo; (3) script com DROP/ALTER COLUMN/TRUNCATE/DELETE/UPDATE é recusado sem <c>CENTRAL_MIGRATE_ALLOW_DESTRUCTIVE=YES</c>;
/// (4) a saída traz só ids de migration (nunca conexão, host ou usuário). Retorna 0 em sucesso/nada a fazer e 1 em recusa ou falha.
/// </summary>
public static partial class MigrationRunner
{
    public const string ConfirmVariable = "CENTRAL_MIGRATE_CONFIRM";
    public const string DestructiveVariable = "CENTRAL_MIGRATE_ALLOW_DESTRUCTIVE";

    [GeneratedRegex(@"\b(DROP\s+(TABLE|COLUMN|SCHEMA|INDEX|CONSTRAINT)|ALTER\s+TABLE\s+\S+\s+ALTER\s+COLUMN|ALTER\s+COLUMN|TRUNCATE|DELETE\s+FROM|UPDATE\s+\S+\s+SET)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Destructive();

    public static IReadOnlyList<string> FindDestructive(string script) =>
        Destructive().Matches(script.Replace("DELETE FROM \"__EFMigrationsHistory\"", "", StringComparison.OrdinalIgnoreCase))
            .Select(match => match.Value.ToUpperInvariant()).Distinct().ToList();

    public static async Task<MigrationPlan> PlanAsync(CentralContingenciaDbContext context, CancellationToken token)
    {
        var all = context.Database.GetMigrations().ToList();
        var applied = (await context.Database.GetAppliedMigrationsAsync(token)).ToList();
        var pending = all.Except(applied).ToList();
        // Banco sem nenhuma migration aplicada (ex.: PostgreSQL novo): não há dados a destruir; as migrations históricas trocam constraints em tabelas vazias.
        var script = pending.Count == 0 || applied.Count == 0 ? "" : context.GetService<IMigrator>().GenerateScript(applied.LastOrDefault(), null);
        return new(applied, pending, all.Last(), FindDestructive(script));
    }

    public static async Task<int> RunAsync(CentralContingenciaDbContext context, bool planOnly, string? confirm, string? allowDestructive, TextWriter output, CancellationToken token)
    {
        MigrationPlan plan;
        try { plan = await PlanAsync(context, token); }
        catch (Exception error) when (error is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            await output.WriteLineAsync($"Falha ao ler o estado das migrations ({error.GetType().Name}). Verifique a conexão com o banco.");
            return 1;
        }
        await output.WriteLineAsync($"Migrations aplicadas: {plan.Applied.Count}. Pendentes: {plan.Pending.Count}. Alvo desta imagem: {plan.Target}.");
        foreach (var id in plan.Pending) await output.WriteLineAsync($"  pendente: {id}");
        if (plan.IsDestructive) await output.WriteLineAsync($"ATENÇÃO: o script contém operações potencialmente destrutivas: {string.Join(", ", plan.DestructiveFindings)}.");
        if (plan.Pending.Count == 0) { await output.WriteLineAsync("Nada a aplicar."); return 0; }
        if (planOnly) { await output.WriteLineAsync("Somente plano: nada foi aplicado."); return 0; }
        if (confirm != plan.Target)
        {
            await output.WriteLineAsync($"Recusado: defina {ConfirmVariable}={plan.Target} para confirmar que o plano acima foi revisado.");
            return 1;
        }
        if (plan.IsDestructive && allowDestructive != "YES")
        {
            await output.WriteLineAsync($"Recusado: o plano é destrutivo; exige {DestructiveVariable}=YES além da confirmação (faça backup antes).");
            return 1;
        }
        try
        {
            await context.Database.MigrateAsync(token);
            var remaining = await context.Database.GetPendingMigrationsAsync(token);
            if (remaining.Any()) { await output.WriteLineAsync("Falha: ainda há migrations pendentes após a aplicação."); return 1; }
        }
        catch (Exception error) when (error is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            await output.WriteLineAsync($"Falha ao aplicar migrations ({error.GetType().Name}). O banco pode estar parcialmente atualizado; consulte o plano e restaure o backup se necessário.");
            return 1;
        }
        await output.WriteLineAsync($"Migrations aplicadas com sucesso até {plan.Target}.");
        return 0;
    }
}
