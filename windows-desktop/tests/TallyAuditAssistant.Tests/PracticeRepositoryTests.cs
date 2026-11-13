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
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
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
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
