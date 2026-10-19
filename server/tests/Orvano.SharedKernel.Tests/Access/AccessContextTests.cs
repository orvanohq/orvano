using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Access;

using Shouldly;

using Xunit;

using static Orvano.SharedKernel.Tests.Access.Callers;

namespace Orvano.SharedKernel.Tests.Access;

// Spec 0012 PRJ-3: the access layer sets the caller and the project once per request.
public sealed class AccessContextTests
{
    private static readonly Guid OtherProject = Guid.Parse("0192f7a4-0000-7000-8000-000000000002");

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    public async Task Both_contexts_read_what_the_access_layer_set_when_in_one_request_scope()
    {
        await using ServiceProvider services = new ServiceCollection().AddAccessContext().BuildServiceProvider();
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<AccessContext>().Establish(A, Project);

        scope.ServiceProvider.GetRequiredService<ICallerContext>().Current.ShouldBeSameAs(A);
        scope.ServiceProvider.GetRequiredService<IProjectContext>().Id.ShouldBe(Project);
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    public async Task Each_request_scope_gets_its_own_context_when_resolved()
    {
        await using ServiceProvider services = new ServiceCollection().AddAccessContext().BuildServiceProvider();
        await using AsyncServiceScope first = services.CreateAsyncScope();
        await using AsyncServiceScope second = services.CreateAsyncScope();

        first.ServiceProvider.GetRequiredService<AccessContext>().Establish(A, Project);

        Should.Throw<InvalidOperationException>(() => second.ServiceProvider.GetRequiredService<ICallerContext>().Current);
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    public void Project_can_be_set_after_the_caller_when_the_same_caller_is_passed_again()
    {
        var context = new AccessContext();

        context.Establish(Operator, null);
        Should.Throw<InvalidOperationException>(() => context.Id);
        context.Establish(Operator, Project);

        context.Current.ShouldBeSameAs(Operator);
        context.Id.ShouldBe(Project);
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    public void Establish_throws_and_keeps_the_first_caller_when_a_different_caller_is_set()
    {
        var context = new AccessContext();
        context.Establish(A, null);

        Should.Throw<InvalidOperationException>(() => context.Establish(B, null));

        context.Current.ShouldBeSameAs(A);
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    public void Establish_throws_even_when_a_different_caller_has_the_same_values()
    {
        var context = new AccessContext();
        context.Establish(Caller.SecretKey(Project, [Scopes.StorageRead]), null);

        Should.Throw<InvalidOperationException>(() => context.Establish(Caller.SecretKey(Project, [Scopes.StorageRead]), null));
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    public void Establish_throws_and_keeps_the_first_project_when_a_different_project_is_set()
    {
        var context = new AccessContext();
        context.Establish(A, Project);

        Should.Throw<InvalidOperationException>(() => context.Establish(A, OtherProject));

        context.Id.ShouldBe(Project);
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    public void Establish_keeps_the_project_when_called_again_with_the_same_project_or_none()
    {
        var context = new AccessContext();
        context.Establish(A, Project);

        context.Establish(A, Project);
        context.Establish(A, null);

        context.Id.ShouldBe(Project);
    }

    [Fact]
    [Trait("Spec", "0012:AC-9")]
    public void Establish_throws_when_the_caller_is_null() =>
        Should.Throw<ArgumentNullException>(() => new AccessContext().Establish(null!, Project));
}
