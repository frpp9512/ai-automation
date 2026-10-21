using AiAutomations.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace AiAutomations.Infrastructure.Data;

/// <summary>
/// The user's known repositories, seeded once on first run of any host (Server, Desktop) so
/// the AI has something to match TaskBrief/Ticket suggestions against from the start. Shared
/// here instead of duplicated per composition root so the list stays in sync everywhere.
/// </summary>
public static class DefaultRepositorySeed
{
    public static async Task EnsureSeededAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Repositories.AnyAsync(cancellationToken))
        {
            return;
        }

        db.Repositories.AddRange(
            new RepositoryProfile
            {
                Name = "player-ui",
                Path = @"X:\xt\player-ui",
                Keywords = "blazor webassembly, player ui, sports betting, betslip, fixtures, odds, betting components, " +
                           "account, layout, bfa, webclient, liveclient, dgsclient, oidc, keycloak, signalr, multi-tenant, " +
                           "feature flags, theming, localization, white-label operators"
            },
            new RepositoryProfile
            {
                Name = "xtos-configs-api",
                Path = @"X:\xt\xtos-configs-api",
                Keywords = "configuration api, runtime settings, themes, feature flags, deployment sites, " +
                           "blazor webassembly admin, ef core, client config, clients config"
            },
            new RepositoryProfile
            {
                Name = "xtos-linehouse-icons",
                Path = @"X:\xt\xtos-linehouse-icons",
                Keywords = "icons, contestant icons, league icons, linehouse, oddsservice, cdn, sql server, dapper, " +
                           "redis, hybridcache, aws s3, fluentui"
            },
            new RepositoryProfile
            {
                Name = "xtos-thirdparty-internal-do",
                Path = @"X:\xt\xtos-thirdparty-internal-do",
                Keywords = "third-party sso, authentication integration, casino, lottery, sportsbook, cashier, " +
                           "live betting, propbuilder, racebook, lotto, webapido, webapiinternal, dgsdata"
            },
            new RepositoryProfile
            {
                Name = "xtos-trifecta-auth",
                Path = @"X:\xt\xtos-trifecta-auth",
                Keywords = "authentication library, oidc login, keycloak, token-based auth, blazor wasm auth, " +
                           "mvc, razor pages, blazor server, minimal apis, reusable auth building blocks"
            },
            new RepositoryProfile
            {
                Name = "xtos-grading-dashboard",
                Path = @"X:\xt\xtos-grading-dashboard",
                Keywords = "grading dashboard, bet grading, settle bets, results, admin dashboard, api, client"
            });

        await db.SaveChangesAsync(cancellationToken);
    }
}
