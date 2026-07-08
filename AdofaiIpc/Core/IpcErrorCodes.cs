namespace AdofaiIpc.Core;

public static class IpcErrorCodes
{
  public const string InvalidRequest = "invalid_request";
  public const string InvalidNamespace = "invalid_namespace";
  public const string InvalidMethod = "invalid_method";
  public const string NamespaceNotFound = "namespace_not_found";
  public const string HandlerNotFound = "handler_not_found";
  public const string HandlerFailed = "handler_failed";
  public const string InternalError = "internal_error";
  public const string OriginNotAllowed = "origin_not_allowed";
}
