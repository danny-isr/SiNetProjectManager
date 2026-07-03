namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>Describes the locally configured AccService API key without exposing the secret itself.</summary>
public interface IAccServiceKeyDiagnostics
{
    AccServiceKeyInfo Describe();
}
