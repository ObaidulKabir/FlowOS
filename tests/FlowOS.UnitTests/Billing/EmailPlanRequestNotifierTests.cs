using FlowOS.Application.Billing;
using FlowOS.Infrastructure.Services;
using FlowOS.Infrastructure.Services.Communication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlowOS.UnitTests.Billing;

public class EmailPlanRequestNotifierTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task AdminNotice_GoesToConfiguredAddress_AndIsSkippedWhenUnset()
    {
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.SendEmailAsync(It.IsAny<EmailSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailSendResult(true));
        var request = new PlanRequestDto { RequestedPlan = "Team", PaymentReference = "TRX-1" };

        await Create(email, "ops@example.test").NotifyAdminOfRequestAsync(TenantId, "Acme", request);
        await Create(email, null).NotifyAdminOfRequestAsync(TenantId, "Acme", request);

        email.Verify(e => e.SendEmailAsync(
            It.Is<EmailSendRequest>(r => r.To == "ops@example.test" && r.Body.Contains("TRX-1") && r.Subject.Contains("Acme")),
            It.IsAny<CancellationToken>()), Times.Once);
        email.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Approved", "is now active")]
    [InlineData("Rejected", "was not approved")]
    public async Task TenantNotice_UsesBillingContact_AndStatusWording(string status, string expected)
    {
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.SendEmailAsync(It.IsAny<EmailSendRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailSendResult(true));
        var request = new PlanRequestDto
        {
            RequestedPlan = "Team",
            Status = status,
            BillingContactEmail = "billing@acme.test",
            AdminNotes = "ref unclear"
        };

        await Create(email, null).NotifyTenantOfDecisionAsync(TenantId, "Acme", request);

        email.Verify(e => e.SendEmailAsync(
            It.Is<EmailSendRequest>(r => r.To == "billing@acme.test" && r.Body.Contains(expected) && r.Body.Contains("ref unclear")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TenantNotice_IsSkipped_WithoutBillingContact_AndSenderFailureNeverThrows()
    {
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.SendEmailAsync(It.IsAny<EmailSendRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));
        var notifier = Create(email, "ops@example.test");

        await notifier.NotifyTenantOfDecisionAsync(TenantId, "Acme", new PlanRequestDto { Status = "Approved" });
        email.VerifyNoOtherCalls();

        await notifier.NotifyAdminOfRequestAsync(TenantId, "Acme", new PlanRequestDto { RequestedPlan = "Team" });
    }

    private static EmailPlanRequestNotifier Create(Mock<IEmailSender> email, string? adminAddress)
    {
        var values = new Dictionary<string, string?>();
        if (adminAddress != null)
            values[EmailPlanRequestNotifier.AdminEmailSetting] = adminAddress;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new EmailPlanRequestNotifier(email.Object, config, NullLogger<EmailPlanRequestNotifier>.Instance);
    }
}
