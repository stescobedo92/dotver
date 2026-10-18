namespace DotVer.Domain;

public enum GlobalJsonReadStatus
{
    Pin,
    NoPin,
    Invalid
}

public sealed record GlobalJsonReadResult(GlobalJsonReadStatus Status, GlobalJsonRequest? Request);
