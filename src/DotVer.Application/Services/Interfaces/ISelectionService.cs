using DotVer.Domain;

namespace DotVer.Application;

public interface ISelectionService
{
    Task<SdkSelection> ResolveAsync(CancellationToken cancellationToken);

    /// <summary>Resolves like <see cref="ResolveAsync"/> but returns every step of the chain and never throws for an unmatched DOTVER_VERSION.</summary>
    Task<SelectionTrace> ExplainAsync(CancellationToken cancellationToken);
}
