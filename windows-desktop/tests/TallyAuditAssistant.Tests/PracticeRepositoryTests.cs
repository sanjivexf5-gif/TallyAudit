using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.Core.Domain.Practice;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;

namespace TallyAuditAssistant.Tests;

public sealed class PracticeRepositoryTests
{
    [Fact]
    public async Task ClientEntityAndEngagement_ArePersistedAndFilteredIndependentlyOfTallyCompanies()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TallyAuditPracticeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "practice.db");

        try
        {
            var factory = new SqliteConnectionFactory(databasePath);
            var initializer = new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance, databasePath);
            await initializer.ApplyMigrationsAsync();
            var repository = new PracticeRepository(factory, NullLogger<PracticeRepository>.Instance);

            var client = new PracticeClient
            {
                ClientCode = "CLIENT-001",
                DisplayName = "Example Manufacturing Private Limited",
                ClientType = "Company",
                PAN = "AABCE1234F"
            };
            Assert.True(await repository.CreateClientAsync(client));

            var entity = new PracticeClientEntity
            {
                ClientId = client.Id,
                LegalName = "Example Manufacturing Private Limited",
                PAN = "AABCE1234F",
                GSTIN = "19AABCE1234F1Z5",
                StateCode = "19"
            };
            Assert.True(await repository.CreateClientEntityAsync(entity));

            var otherClient = new PracticeClient
            {
                ClientCode = "CLIENT-002",
                DisplayName = "Unrelated Client",
                Status = "Inactive"
            };
            Assert.True(await repository.CreateClientAsync(otherClient));
            var otherEntity = new PracticeClientEntity
            {
                ClientId = otherClient.Id,
                LegalName = "Unrelated Client Entity"
            };
            Assert.True(await repository.CreateClientEntityAsync(otherEntity));

            var invalidCrossClientEngagement = new ClientEngagement
            {
                ClientId = client.Id,
                ClientEntityId = otherEntity.Id,
                ServiceCode = "GST-RET",
                ServiceName = "GST return preparation",
                FinancialYear = "2026-27"
            };
            await Assert.ThrowsAsync<ArgumentException>(() => repository.CreateEngagementAsync(invalidCrossClientEngagement));

            var engagement = new ClientEngagement
            {
                ClientId = client.Id,
                ClientEntityId = entity.Id,
                ServiceCode = "GST-RET",
                ServiceName = "GST return preparation",
                FinancialYear = "2026-27",
                Status = "In Progress",
                DueDate = new DateTime(2026, 12, 11),
                ProfessionalFee = 15000m
            };
            Assert.True(await repository.CreateEngagementAsync(engagement));

            var clients = await repository.GetClientsAsync();
            var entities = await repository.GetClientEntitiesAsync(client.Id);
            var engagements = await repository.GetEngagementsAsync(client.Id, "In Progress");

            Assert.Single(clients);
            Assert.Equal(client.Id, clients[0].Id);
            Assert.Single(entities);
            Assert.Equal("19AABCE1234F1Z5", entities[0].GSTIN);
            Assert.Single(engagements);
            Assert.Equal(entity.Id, engagements[0].ClientEntityId);
            Assert.Equal(15000m, engagements[0].ProfessionalFee);

            engagement.Status = "Completed";
            Assert.True(await repository.UpdateEngagementAsync(engagement));
            Assert.Empty(await repository.GetEngagementsAsync(client.Id, "In Progress"));
            Assert.Equal("Completed", (await repository.GetEngagementByIdAsync(engagement.Id))?.Status);
        }
        finally
        {
            // Microsoft.Data.Sqlite pools handles after Dispose by default. Clear the
            // per-process pools before deleting the temporary database and WAL files.
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }


    [Fact]
    public async Task RecurringTemplates_AreClientScopedAndCanBeDeactivated()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TallyAuditPracticeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "practice.db");
        try
        {
            var factory = new SqliteConnectionFactory(databasePath);
            await new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance, databasePath).ApplyMigrationsAsync();
            var repository = new PracticeRepository(factory, NullLogger<PracticeRepository>.Instance);
            var client = new PracticeClient { ClientCode = "RECUR-001", DisplayName = "Recurring Client" };
            var other = new PracticeClient { ClientCode = "RECUR-002", DisplayName = "Other Client" };
            Assert.True(await repository.CreateClientAsync(client));
            Assert.True(await repository.CreateClientAsync(other));
            var entity = new PracticeClientEntity { ClientId = client.Id, LegalName = "Recurring Entity" };
            var otherEntity = new PracticeClientEntity { ClientId = other.Id, LegalName = "Other Entity" };
            Assert.True(await repository.CreateClientEntityAsync(entity));
            Assert.True(await repository.CreateClientEntityAsync(otherEntity));
            var invalid = new RecurringComplianceTaskTemplate { ClientId = client.Id, ClientEntityId = otherEntity.Id,
                ServiceCode = "GST-3B", TaskName = "Monthly GSTR-3B", Frequency = "Monthly", IntervalMonths = 1,
                NextDueDate = new DateTime(2026, 12, 20) };
            await Assert.ThrowsAsync<ArgumentException>(() => repository.CreateRecurringTaskTemplateAsync(invalid));
            var template = new RecurringComplianceTaskTemplate { ClientId = client.Id, ClientEntityId = entity.Id,
                ServiceCode = "GST-3B", TaskName = "Monthly GSTR-3B", Frequency = "Monthly", IntervalMonths = 1,
                NextDueDate = new DateTime(2026, 12, 20), ReminderLeadDays = 5 };
            Assert.True(await repository.CreateRecurringTaskTemplateAsync(template));
            Assert.Single(await repository.GetRecurringTaskTemplatesAsync(client.Id));
            Assert.Empty(await repository.GetRecurringTaskTemplatesAsync(other.Id));
            template.IsActive = false;
            Assert.True(await repository.UpdateRecurringTaskTemplateAsync(template));
            Assert.Empty(await repository.GetRecurringTaskTemplatesAsync(client.Id));
            var inactive = await repository.GetRecurringTaskTemplatesAsync(client.Id, includeInactive: true);
            Assert.Single(inactive);
            Assert.False(inactive[0].IsActive);
            Assert.Equal(5, inactive[0].ReminderLeadDays);
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RecurringTemplate_RejectsMismatchedFrequencyIntervalAndReminderWindow()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TallyAuditPracticeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "practice.db");
        try
        {
            var factory = new SqliteConnectionFactory(databasePath);
            await new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance, databasePath).ApplyMigrationsAsync();
            var repository = new PracticeRepository(factory, NullLogger<PracticeRepository>.Instance);
            var client = new PracticeClient { ClientCode = "RECUR-003", DisplayName = "Validation Client" };
            Assert.True(await repository.CreateClientAsync(client));
            var template = new RecurringComplianceTaskTemplate { ClientId = client.Id, ServiceCode = "TDS-26Q",
                TaskName = "Quarterly TDS", Frequency = "Quarterly", IntervalMonths = 2,
                NextDueDate = new DateTime(2026, 12, 20) };
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.CreateRecurringTaskTemplateAsync(template));
            template.IntervalMonths = 3; template.ReminderLeadDays = 120;
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.CreateRecurringTaskTemplateAsync(template));
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RecurringGeneration_IsIdempotentAdvancesTemplateAndPreservesMonthEnd()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TallyAuditPracticeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "practice.db");
        try
        {
            var factory = new SqliteConnectionFactory(databasePath);
            await new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance, databasePath).ApplyMigrationsAsync();
            var repository = new PracticeRepository(factory, NullLogger<PracticeRepository>.Instance);
            var client = new PracticeClient { ClientCode = "GEN-001", DisplayName = "Generation Client" };
            Assert.True(await repository.CreateClientAsync(client));
            var entity = new PracticeClientEntity { ClientId = client.Id, LegalName = "Generation Entity" };
            Assert.True(await repository.CreateClientEntityAsync(entity));
            var template = new RecurringComplianceTaskTemplate
            {
                ClientId = client.Id, ClientEntityId = entity.Id, ServiceCode = "GST-3B",
                TaskName = "Monthly GSTR-3B", Frequency = "Monthly", IntervalMonths = 1,
                NextDueDate = new DateTime(2026, 1, 31), ReminderLeadDays = 5
            };
            Assert.True(await repository.CreateRecurringTaskTemplateAsync(template));

            Assert.Equal(3, await repository.GenerateRecurringEngagementsAsync(new DateTime(2026, 3, 31)));
            Assert.Equal(0, await repository.GenerateRecurringEngagementsAsync(new DateTime(2026, 3, 31)));
            var engagements = await repository.GetEngagementsAsync(client.Id);
            Assert.Equal(3, engagements.Count);
            Assert.Equal(new[] { new DateTime(2026, 1, 31), new DateTime(2026, 2, 28), new DateTime(2026, 3, 31) },
                engagements.OrderBy(e => e.DueDate).Select(e => e.DueDate!.Value).ToArray());
            Assert.All(engagements, e =>
            {
                Assert.Equal("2025-26", e.FinancialYear);
                Assert.Equal("Open", e.Status);
                Assert.Equal(entity.Id, e.ClientEntityId);
                Assert.Contains(template.Id, e.Notes);
            });
            var advanced = Assert.Single(await repository.GetRecurringTaskTemplatesAsync(client.Id));
            Assert.Equal(new DateTime(2026, 4, 30), advanced.NextDueDate);
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CreateClient_RejectsMissingRequiredIdentityFields()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TallyAuditPracticeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "practice.db");

        try
        {
            var factory = new SqliteConnectionFactory(databasePath);
            await new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance, databasePath).ApplyMigrationsAsync();
            var repository = new PracticeRepository(factory, NullLogger<PracticeRepository>.Instance);

            await Assert.ThrowsAsync<ArgumentException>(() => repository.CreateClientAsync(new PracticeClient
            {
                ClientCode = " ",
                DisplayName = " "
            }));
        }
        finally
        {
            // Microsoft.Data.Sqlite pools handles after Dispose by default. Clear the
            // per-process pools before deleting the temporary database and WAL files.
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }


    [Fact]
    public void EngagementDueStatus_IdentifiesOverdueDueTodayUpcomingAndClosedItems()
    {
        var today = DateTime.Today;
        var overdue = new ClientEngagement { Status = "Open", DueDate = today.AddDays(-3) };
        var dueToday = new ClientEngagement { Status = "In Progress", DueDate = today };
        var upcoming = new ClientEngagement { Status = "Open", DueDate = today.AddDays(5) };
        var later = new ClientEngagement { Status = "Open", DueDate = today.AddDays(20) };
        var completed = new ClientEngagement { Status = "Completed", DueDate = today.AddDays(-10) };
        var cancelled = new ClientEngagement { Status = "Cancelled", DueDate = today.AddDays(-1) };
        var noDate = new ClientEngagement { Status = "Open" };

        Assert.Equal("Overdue by 3 day(s)", overdue.DueStatus);
        Assert.True(overdue.IsOverdue);
        Assert.Equal("Due today", dueToday.DueStatus);
        Assert.False(dueToday.IsOverdue);
        Assert.Equal("Due in 5 day(s)", upcoming.DueStatus);
        Assert.Equal("Upcoming", later.DueStatus);
        Assert.Equal("Completed", completed.DueStatus);
        Assert.False(completed.IsOverdue);
        Assert.Equal("Cancelled", cancelled.DueStatus);
        Assert.False(cancelled.IsOverdue);
        Assert.Equal("No due date", noDate.DueStatus);
        Assert.Null(noDate.DaysUntilDue);
    }
}
