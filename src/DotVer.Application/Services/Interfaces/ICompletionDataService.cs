namespace DotVer.Application;

public interface ICompletionDataService
{
    Task<IReadOnlyList<string>> GetCandidatesAsync(string kind, CancellationToken cancellationToken);
}
