// Hand-written designer partial: authored without the EF tooling available (see
// M003_AddAcknowledgement.Designer.cs) — without these two attributes EF Core does not
// discover the migration and Database.Migrate() applies nothing.
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceSentry.Modules.Alerts.Migrations
{
    [DbContext(typeof(AlertsDbContext))]
    [Migration("20260926000000_M004_AddOccurrenceCounter")]
    partial class M004_AddOccurrenceCounter
    {
    }
}
