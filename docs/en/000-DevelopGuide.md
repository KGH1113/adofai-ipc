# AdofaiIpc Developer Guide

[Korean](../kor/000-DevelopGuide.md)

## Introduction

AdofaiIpc is a local IPC gateway that lets multiple A Dance of Fire and Ice mods share one
HTTP listener. Each mod registers methods under its own namespace, while browser tools,
userscripts, CLIs, and other local clients call those methods through a common protocol.

AdofaiIpc runs directly through UnityModManager and does not require JALib.

Register a namespace when your mod is enabled and unregister it when your mod is disabled.
Use `RegisterMainThread` for handlers that access Unity or ADOFAI state.

## Contents

1. [Getting Started](001-GettingStarted.md)
2. [Mod API](002-ModApi.md)
3. [HTTP Protocol](003-HttpProtocol.md)
4. [Lifecycle](004-Lifecycle.md)
5. [JavaScript/TypeScript Client](005-JsClient.md)
