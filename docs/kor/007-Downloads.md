# 스트리밍 다운로드

[목차](000-DevelopGuide.md) | [English](../en/007-Downloads.md)

파일 본문 대신 이름 있는 명령 결과로 1회용 HTTP 다운로드 티켓을 전달합니다.

```csharp
feature.OnCommand("recording.export", IpcExecutionContext.Background, command => {
    var file = recordings.Find((string)JObject.Parse(command.Json)["recordingId"]);
    if (file == null) {
        command.Reject("recording_missing", "다시 녹화해 주세요.");
        return Task.CompletedTask;
    }
    command.ReplyDownload("recording.ready", file.OpenRead(), file.ByteLength,
        "recording.wav", "audio/wav", "{\"revision\":1}");
    return Task.CompletedTask;
});
```

ReplyDownload는 읽기 Stream 또는 Action<Stream>을 받습니다.
실패·취소 경로를 포함해 source 소유권을 런타임에 넘깁니다.
writer는 HTTP worker에서 정확한 byteLength를 쓰고 응답 stream을 닫거나 Unity를 접근하지 않습니다.

결과에는 {url, byteLength, metadata}와 명령 correlationId를 담습니다. 메타데이터는 작게 유지합니다.
64 KiB chunk마다 flush하고 Content-Disposition·X-Byte-Length·no-store·nosniff를 설정합니다.
URL을 attachment로 열거나 허용된 브라우저에서 fetch합니다.

티켓은 60초·1회용이며 최대 128개, 동시 다운로드 4개로 제한합니다.
기능 Dispose와 호스트 종료는 미사용 티켓을 폐기합니다.
발급 당시 복사한 Origin 목록으로 검증하며 명시적으로 허용된 브라우저 Origin만 CORS 읽기를 허용합니다.
거부한 Origin은 티켓을 소비하지 않습니다. native/attachment 요청은 Origin이 없을 수 있습니다.
URL을 발급 앱 흐름에 속한 임시 bearer capability로 다룹니다.

큰 제어 결과는 연결을 유지한 채 correlated command.failed/message_too_large로 전달합니다.
파일 본문은 다운로드 stream에만 넣습니다.
