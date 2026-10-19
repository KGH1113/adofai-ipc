namespace AdofaiIpc.Loader;
internal static class RuntimeLoaderComposition {
  internal static readonly RuntimeLoader Loader = new RuntimeLoader(new RuntimeCatalog(), new RuntimeActivator(), new ProcessRuntimeState());
}
