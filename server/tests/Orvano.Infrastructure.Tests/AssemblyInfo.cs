using Orvano.Testing;

using Xunit;

// Spec 0002 TST-5: one PostgreSQL container for the whole run.
[assembly: AssemblyFixture(typeof(PostgresFixture))]
