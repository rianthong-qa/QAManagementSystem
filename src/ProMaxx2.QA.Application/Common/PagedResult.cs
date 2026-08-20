namespace ProMaxx2.QA.Application.Common;
public sealed record PagedResult<T>(int Total, IReadOnlyList<T> Rows)
{
    public static PagedResult<T> Clamp(int page, int size)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 20, 100);
        return new(0, Array.Empty<T>());
    }
}
