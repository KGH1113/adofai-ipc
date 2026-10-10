# Mod API

[Guide](000-DevelopGuide.md) | [한국어](../kor/002-ModApi.md)

Consumers reference only Contracts and Loader. The contract contains no Unity, Newtonsoft.Json
or consumer DTO types. Serialize your domain payloads to JSON in the IPC adapter.

```csharp
using System.Threading.Tasks;
using AdofaiIpc.Contracts;
using AdofaiIpc.Loader;
using Newtonsoft.Json.Linq;

IIpcRuntime runtime = IpcLoader.GetRuntime(modsDirectory,
    new RuntimeRequirements("2.0.0", requiredCapabilities: new[] { "commands", "game-thread" }));

IFeatureRegistration registration = runtime.RegisterFeature(
    new FeatureDescription("example-mod", "1.0.0", protocolMajor: 1),
    feature => {
        feature.OnCommand("level.open", IpcExecutionContext.GameThread, command => {
            string id = (string)JObject.Parse(command.Json)["levelId"];
            if (string.IsNullOrEmpty(id)) {
                command.Reject("invalid_level", "Choose a level first.");
                return Task.CompletedTask;
            }
            OpenLevel(id);
            command.Reply("level.opened", new JObject { ["levelId"] = id }.ToString());
            return Task.CompletedTask;
        });
        feature.OnClientAttached(client =>
            client.Send("library.snapshot", "{\"revision\":1}"));
    });
registration.MarkReady();

// On a domain change:
registration.Publish("library.changed", "{\"revision\":2}");
// On disable/unload:
registration.Dispose();
```

Registration starts initializing. Duplicate feature names fail rather than replacing another
mod's registration. A failed configure callback rolls back only that registration.
The builder is valid only during configure. Dispose is idempotent and cancels command tokens,
revokes tickets and publishes unavailable. Re-enable by creating a new registration.

OnCommand takes an asynchronous handler. GameThread selects the initial invocation thread;
Background uses a worker. Reply sends a named event to the issuing client with the command ID
as correlationId. Reject can include a third detailsJson argument. accepted is admission, not
completion; a thrown handler error produces command.failed. Returning silently sends no result.

OnClientAttached, OnClientDetached and OnEvent execute on the game thread. Attached fires only
after readiness; use it to send the current snapshot for initial subscriptions and reconnects.
Use command.Client identities for ownership. Cancellation follows disconnect, unsubscribe,
feature disposal and host shutdown. Running handlers must observe their cancellation token.

Publish broadcasts to authorized subscribers. SendToPeer targets one subscriber;
SendCommandToPeer returns a reverse command ID. Replies arrive through OnEvent with CorrelationId.
CreateLocalPeer returns the same router without socket I/O: install Message, then Subscribe,
SendCommand/SendEvent, Unsubscribe and Dispose. Local Message runs on a worker.

Feature names use lowercase letters, digits and hyphens; message names also allow dots.
AllowedOrigins restricts messaging. Native peers without Origin remain allowed.
