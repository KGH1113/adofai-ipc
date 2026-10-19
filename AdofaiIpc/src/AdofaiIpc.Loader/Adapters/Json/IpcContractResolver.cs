using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace AdofaiIpc;

internal sealed class IpcContractResolver : DefaultContractResolver
{
  internal IpcContractResolver() { NamingStrategy = new CamelCaseNamingStrategy { OverrideSpecifiedNames = false, ProcessDictionaryKeys = false }; }
  protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
  {
    var property = base.CreateProperty(member, memberSerialization);
    if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(IpcOptional<>))
    {
      var previous = property.ShouldSerialize;
      var specified = property.PropertyType.GetProperty(nameof(IpcOptional<int>.IsSpecified));
      property.ShouldSerialize = instance => (previous == null || previous(instance)) && (bool)specified.GetValue(property.ValueProvider.GetValue(instance));
    }
    return property;
  }
}
