using Testcontainers.MsSql;
using Xunit;

namespace Quotes.Tests.Integration.Testcontainers;

/// <summary>
/// Starts one SQL Server 2022 container for the whole test run and tears it down when the
/// run finishes. Spinning up a fresh container per test would make the suite prohibitively
/// slow, so instead every test asks this fixture for the container's connection string and
/// then opens its own uniquely named database on that shared instance (see
/// <see cref="IntegrationTestFactory"/>) - the server is shared, the schema/data are not.
/// </summary>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture>
{
    public const string Name = "SqlServer container";
}
