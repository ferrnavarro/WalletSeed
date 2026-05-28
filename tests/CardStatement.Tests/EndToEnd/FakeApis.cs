using CardStatement.Core.Abstractions;
using CardStatement.Core.Models;

namespace CardStatement.Tests.EndToEnd;

internal sealed class FakeCategoryApi : ICategoryApi
{
    private readonly IReadOnlyList<Category> _categories;
    public FakeCategoryApi(IReadOnlyList<Category> categories) => _categories = categories;
    public Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(_categories);
}

internal sealed class FakeLabelsApi : ILabelsApi
{
    private readonly IReadOnlyList<Label> _labels;
    public FakeLabelsApi(IReadOnlyList<Label> labels) => _labels = labels;
    public Task<IReadOnlyList<Label>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(_labels);
}
