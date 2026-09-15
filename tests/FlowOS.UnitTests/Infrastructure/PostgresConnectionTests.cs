using FlowOS.Infrastructure.Persistence;

namespace FlowOS.UnitTests.Infrastructure;

public class PostgresConnectionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Host=;Port=5432;Database=flowos")]
    [InlineData("Host= ;Port=5432;Database=flowos")]
    [InlineData("Port=5432;Database=flowos")]
    public void Empty_or_missing_host_is_not_usable(string? connectionString)
    {
        Assert.False(PostgresConnection.HasUsableHost(connectionString));
    }

    [Theory]
    [InlineData("Host=postgres;Port=5432;Database=flowos;Username=root;Password=toor")]
    [InlineData("Server=db.internal;Database=flowos")]
    public void Present_host_is_usable(string connectionString)
    {
        Assert.True(PostgresConnection.HasUsableHost(connectionString));
    }
}
