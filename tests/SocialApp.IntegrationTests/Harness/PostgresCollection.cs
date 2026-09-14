namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// Collection chia chung <see cref="PostgresFixture"/>. Lớp test cần Postgres thật khai
/// <c>[Collection(PostgresCollection.Name)]</c> và nhận <see cref="PostgresFixture"/> qua constructor —
/// quên attribute thì xUnit báo "constructor parameters did not have matching fixture data".
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
