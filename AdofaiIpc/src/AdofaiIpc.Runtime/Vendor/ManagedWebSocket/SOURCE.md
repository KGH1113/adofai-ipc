Source: https://github.com/sta/websocket-sharp
Commit: f7904e6afb934cfff9eda1f29ee6c1291b14c4bc
License: MIT (LICENSE.txt).

Local adaptations: isolated namespace, 2 MiB message/frame limits. Compiled into the runtime; no external WebSocketSharp assembly or Unity stub WebSocket API is used. Downloads use bounded, flushed chunked streaming because upstream fixed-length response streams buffer the complete entity.

Additional Mono adaptation: fully-qualified System.Threading.Timer avoids the game assembly global Timer type collision. Source files are embedded directly; preserve LICENSE.txt when redistributing.
