using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Domain.Projects;

namespace ProMaxx2.QA.UnitTests;

public sealed class ModuleVisibilityTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();

    [Fact]
    public async Task Hiding_module_hides_its_descendants_only()
    {
        var (repository, parent, child, grandChild, sibling) = BuildTree();
        var service = new ProjectService(repository);

        await service.DeactivateModuleAsync(parent.ModuleId, null, CancellationToken.None);

        Assert.False(parent.IsActive);
        Assert.False(child.IsActive);
        Assert.False(grandChild.IsActive);
        Assert.True(sibling.IsActive);
    }

    [Fact]
    public async Task Showing_module_restores_hidden_ancestors_and_descendants()
    {
        var (repository, parent, child, grandChild, sibling) = BuildTree();
        var service = new ProjectService(repository);
        await service.DeactivateModuleAsync(parent.ModuleId, null, CancellationToken.None);
        sibling.Deactivate(null);

        await service.ActivateModuleAsync(child.ModuleId, null, CancellationToken.None);

        Assert.True(parent.IsActive);
        Assert.True(child.IsActive);
        Assert.True(grandChild.IsActive);
        Assert.False(sibling.IsActive);
    }

    private static (FakeRepository, ProductModule, ProductModule, ProductModule, ProductModule) BuildTree()
    {
        var parent = new ProductModule(ProjectId, "MOD-001", "Parent", null, null, null, null);
        var child = new ProductModule(ProjectId, "MOD-002", "Child", parent.ModuleId, null, null, null);
        var grandChild = new ProductModule(ProjectId, "MOD-003", "Grand child", child.ModuleId, null, null, null);
        var sibling = new ProductModule(ProjectId, "MOD-004", "Sibling", null, null, null, null);
        return (new FakeRepository([parent, child, grandChild, sibling]), parent, child, grandChild, sibling);
    }

    private sealed class FakeRepository(List<ProductModule> modules) : IProjectRepository
    {
        public Task<IReadOnlyList<ProductModule>> ListModuleEntitiesAsync(Guid projectId, CancellationToken ct) => Task.FromResult<IReadOnlyList<ProductModule>>(modules.Where(x => x.ProjectId == projectId).ToList());
        public Task<ProductModule?> FindModuleAsync(Guid id, CancellationToken ct) => Task.FromResult(modules.FirstOrDefault(x => x.ModuleId == id));
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProjectDto>> ListForUserAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProjectDto?> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ProjectCodeExistsAsync(string code, CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(Project project, CancellationToken ct) => throw new NotSupportedException();
        public Task<Project?> FindAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModuleDto>> ListModulesAsync(Guid projectId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ModuleCodeExistsAsync(Guid projectId, string code, CancellationToken ct) => throw new NotSupportedException();
        public Task AddModuleAsync(ProductModule module, CancellationToken ct) => throw new NotSupportedException();
        public Task AddProjectUserAsync(Guid userId, Guid projectId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListProjectCodesAsync(string prefix, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListModuleCodesAsync(Guid projectId, string prefix, CancellationToken ct) => throw new NotSupportedException();
    }
}
