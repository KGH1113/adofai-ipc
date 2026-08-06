using System;
using System.Collections.Generic;
using System.Linq;
using AdofaiIpc.Unity;
using Newtonsoft.Json;

namespace AdofaiIpc.Core;

public sealed class IpcRegistry
{
  private readonly object _sync = new object();
  private readonly Dictionary<string, RegisteredNamespace> _namespaces =
    new Dictionary<string, RegisteredNamespace>();

  public AdofaiIpcNamespace RegisterNamespace(string name, IpcNamespaceInfo info)
  {
    if (!IpcNameValidator.IsValidNamespace(name))
    {
      throw new ArgumentException("Invalid IPC namespace: " + name, nameof(name));
    }

    lock (_sync)
    {
      info ??= new IpcNamespaceInfo();
      if (string.IsNullOrEmpty(info.DisplayName)) info.DisplayName = name;
      if (string.IsNullOrEmpty(info.Version)) info.Version = "0.0.0";

      if (_namespaces.TryGetValue(name, out RegisteredNamespace registered))
      {
        registered.Info = info;
        registered.Status = IpcNamespaceStatus.Initializing;
        registered.Error = null;
      }
      else
      {
        registered = new RegisteredNamespace(name, info);
        _namespaces.Add(name, registered);
      }

      return new AdofaiIpcNamespace(this, name, info);
    }
  }

  public bool UnregisterNamespace(string name)
  {
    if (!IpcNameValidator.IsValidNamespace(name))
    {
      throw new ArgumentException("Invalid IPC namespace: " + name, nameof(name));
    }

    lock (_sync)
    {
      return _namespaces.Remove(name);
    }
  }

  public void RegisterMethod(
    string namespaceName,
    string method,
    Func<IpcRequest, object> handler,
    bool requiresMainThread)
  {
    if (!IpcNameValidator.IsValidMethod(method))
    {
      throw new ArgumentException("Invalid IPC method: " + method, nameof(method));
    }

    lock (_sync)
    {
      if (!_namespaces.TryGetValue(namespaceName, out RegisteredNamespace registered))
      {
        throw new InvalidOperationException("IPC namespace is not registered: " + namespaceName);
      }

      registered.Methods[method] = new IpcHandler(handler, requiresMainThread);
    }
  }

  public bool UnregisterMethod(string namespaceName, string method)
  {
    if (!IpcNameValidator.IsValidNamespace(namespaceName))
    {
      throw new ArgumentException("Invalid IPC namespace: " + namespaceName, nameof(namespaceName));
    }

    if (!IpcNameValidator.IsValidMethod(method))
    {
      throw new ArgumentException("Invalid IPC method: " + method, nameof(method));
    }

    lock (_sync)
    {
      if (!_namespaces.TryGetValue(namespaceName, out RegisteredNamespace registered))
      {
        return false;
      }

      return registered.Methods.Remove(method);
    }
  }

  public void SetNamespaceInitializing(string name)
  {
    SetNamespaceStatus(name, IpcNamespaceStatus.Initializing, null);
  }

  public IpcNamespaceStatus GetNamespaceStatus(string name)
  {
    if (!IpcNameValidator.IsValidNamespace(name))
    {
      throw new ArgumentException("Invalid IPC namespace: " + name, nameof(name));
    }

    lock (_sync)
    {
      if (!_namespaces.TryGetValue(name, out RegisteredNamespace registered))
      {
        throw new InvalidOperationException("IPC namespace is not registered: " + name);
      }

      return registered.Status;
    }
  }

  public void SetNamespaceReady(string name)
  {
    SetNamespaceStatus(name, IpcNamespaceStatus.Ready, null);
  }

  public void SetNamespaceError(string name, string code, string message)
  {
    if (string.IsNullOrWhiteSpace(code))
    {
      throw new ArgumentException("Namespace error code is required.", nameof(code));
    }

    if (string.IsNullOrWhiteSpace(message))
    {
      throw new ArgumentException("Namespace error message is required.", nameof(message));
    }

    SetNamespaceStatus(
      name,
      IpcNamespaceStatus.Error,
      new NamespaceErrorInfo { Code = code, Message = message });
  }

  public IpcResponse Invoke(IpcRequest request)
  {
    if (request == null)
    {
      return IpcResponse.Fail(null, IpcErrorCodes.InvalidRequest, "Request body is required.");
    }

    if (!IpcNameValidator.IsValidNamespace(request.Namespace))
    {
      return IpcResponse.Fail(
        request.Id,
        IpcErrorCodes.InvalidNamespace,
        "Namespace is missing or invalid.");
    }

    if (!IpcNameValidator.IsValidMethod(request.Method))
    {
      return IpcResponse.Fail(
        request.Id,
        IpcErrorCodes.InvalidMethod,
        "Method is missing or invalid.");
    }

    IpcHandler handler;

    lock (_sync)
    {
      if (!_namespaces.TryGetValue(request.Namespace, out RegisteredNamespace registered))
      {
        return IpcResponse.Fail(
          request.Id,
          IpcErrorCodes.NamespaceNotFound,
          "Namespace not found: " + request.Namespace);
      }

      if (registered.Status == IpcNamespaceStatus.Initializing)
      {
        return IpcResponse.Fail(
          request.Id,
          IpcErrorCodes.NamespaceInitializing,
          "Namespace is initializing: " + request.Namespace);
      }

      if (registered.Status == IpcNamespaceStatus.Error)
      {
        string message = registered.Error?.Message ?? "Namespace initialization failed.";
        return IpcResponse.Fail(
          request.Id,
          IpcErrorCodes.NamespaceError,
          message);
      }

      if (!registered.Methods.TryGetValue(request.Method, out handler))
      {
        return IpcResponse.Fail(
          request.Id,
          IpcErrorCodes.HandlerNotFound,
          "No handler registered for " + request.Namespace + ":" + request.Method);
      }
    }

    try
    {
      object result = handler.RequiresMainThread
        ? MainThreadInvoker.Invoke(() => handler.Invoke(request))
        : handler.Invoke(request);

      return IpcResponse.Success(request.Id, result);
    }
    catch (TimeoutException e)
    {
      Main.Instance?.LogException(e);
      return IpcResponse.Fail(
        request.Id,
        IpcErrorCodes.HandlerFailed,
        "Handler timed out: " + request.Namespace + ":" + request.Method);
    }
    catch (Exception e)
    {
      Main.Instance?.LogException(e);
      return IpcResponse.Fail(
        request.Id,
        IpcErrorCodes.HandlerFailed,
        "Handler failed: " + request.Namespace + ":" + request.Method);
    }
  }

  public List<NamespaceSummary> ListNamespaces()
  {
    lock (_sync)
    {
      return _namespaces.Values
        .OrderBy(item => item.Name)
        .Select(item => new NamespaceSummary
        {
          Name = item.Name,
          DisplayName = item.Info.DisplayName,
          Version = item.Info.Version,
          Status = GetStatusName(item.Status)
        })
        .ToList();
    }
  }

  public NamespaceDetail GetNamespace(string name)
  {
    lock (_sync)
    {
      if (!_namespaces.TryGetValue(name, out RegisteredNamespace registered)) return null;

      return new NamespaceDetail
      {
        Namespace = registered.Name,
        DisplayName = registered.Info.DisplayName,
        Version = registered.Info.Version,
        Status = GetStatusName(registered.Status),
        Error = registered.Error,
        Methods = registered.Methods.Keys.OrderBy(method => method).ToList()
      };
    }
  }

  public IpcNamespaceInfo GetNamespaceInfo(string name)
  {
    lock (_sync)
    {
      if (!_namespaces.TryGetValue(name, out RegisteredNamespace registered))
      {
        return null;
      }

      return registered.Info;
    }
  }

  private void SetNamespaceStatus(
    string name,
    IpcNamespaceStatus status,
    NamespaceErrorInfo error)
  {
    if (!IpcNameValidator.IsValidNamespace(name))
    {
      throw new ArgumentException("Invalid IPC namespace: " + name, nameof(name));
    }

    lock (_sync)
    {
      if (!_namespaces.TryGetValue(name, out RegisteredNamespace registered))
      {
        throw new InvalidOperationException("IPC namespace is not registered: " + name);
      }

      registered.Status = status;
      registered.Error = error;
    }
  }

  private static string GetStatusName(IpcNamespaceStatus status)
  {
    return status switch
    {
      IpcNamespaceStatus.Initializing => "initializing",
      IpcNamespaceStatus.Ready => "ready",
      IpcNamespaceStatus.Error => "error",
      _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
  }

  private sealed class RegisteredNamespace
  {
    public readonly string Name;
    public IpcNamespaceInfo Info;
    public IpcNamespaceStatus Status = IpcNamespaceStatus.Initializing;
    public NamespaceErrorInfo Error;
    public readonly Dictionary<string, IpcHandler> Methods = new Dictionary<string, IpcHandler>();

    public RegisteredNamespace(string name, IpcNamespaceInfo info)
    {
      Name = name;
      Info = info;
    }
  }

  public sealed class NamespaceSummary
  {
    [JsonProperty("name")]
    public string Name;
    [JsonProperty("displayName")]
    public string DisplayName;
    [JsonProperty("version")]
    public string Version;
    [JsonProperty("status")]
    public string Status;
  }

  public sealed class NamespaceDetail
  {
    [JsonProperty("namespace")]
    public string Namespace;
    [JsonProperty("displayName")]
    public string DisplayName;
    [JsonProperty("version")]
    public string Version;
    [JsonProperty("status")]
    public string Status;
    [JsonProperty("error")]
    public NamespaceErrorInfo Error;
    [JsonProperty("methods")]
    public List<string> Methods;
  }

  public sealed class NamespaceErrorInfo
  {
    [JsonProperty("code")]
    public string Code;
    [JsonProperty("message")]
    public string Message;
  }
}
