using DotVer.Domain;

namespace DotVer.Application;

public interface IDoctorService
{
    Task<DoctorResult> DiagnoseAsync(CancellationToken cancellationToken);

    Task<DoctorResult> FixAsync(CancellationToken cancellationToken);
}
