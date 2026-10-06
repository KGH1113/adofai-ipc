# 스트리밍 다운로드

[목차](000-DevelopGuide.md) | [English](../en/007-Downloads.md)

RegisterDownloadCommand로 티켓을 만들고 실제 파일은 HTTP 스트림으로 전송합니다.

```csharp
ipc.RegisterDownloadCommand("recording.export", command => {
    var file = recordings.Find((string)command.Payload["recordingId"]);
    if (file == null) return new IpcDownloadError("recording_missing", "다시 녹화해 주세요.");
    return new IpcDownloadSource(file.OpenRead(), file.ByteLength, "recording.wav", "audio/wav");
});
```

handler는 IpcDownloadSource 또는 IpcDownloadError를 반환합니다. 공개 callback 생성자는
Action<Stream>, byteLength, fileName, contentType을 받습니다. IpcDownloadResponse 편의 API도
유지합니다. callback은 티켓 소비 때 HTTP worker에서 실행되므로 Unity를 직접 접근하지 마세요.
정확한 길이를 쓰고 응답 스트림을 닫지 않습니다. source stream의 수명과 dispose는 티켓이 맡습니다.

발급한 peer에게 correlationId를 가진 download.ready와 소문자 {url,byteLength}를 보냅니다.
url을 attachment 링크로 열면 큰 Blob 없이 브라우저가 저장합니다. 허용된 앱에서는 fetch도
사용할 수 있습니다.64KiB chunk마다 flush하고 Content-Disposition, X-Byte-Length, no-store,
nosniff를 설정합니다. chunked 전송이므로 Content-Length 대신 이벤트의 byteLength를 사용하세요.

티켓은60초·1회용이며 최대128개, 동시 다운로드4개입니다. namespace 해제와 서버 정지 때 취소합니다.
생성 시 복사한 Origin 목록으로 소비 권한을 검증합니다. CORS 읽기는 명시적으로 허용한 Origin의
티켓 소비가 성공한 경우에만 부여하고 제한 없는/native 티켓에는 CORS 헤더를 만들지 않습니다.
불허 Origin은 티켓을 소비하지 않습니다. attachment 이동이나 native 도구는 Origin이 없을 수
있으므로 URL은 짧은 수명의 bearer capability로 취급하고 발급한 앱 흐름 안에서만 사용하세요.

도메인 결과 이름과 작은 메타데이터를 다운로드 티켓에 함께 담을 수 있습니다.

```csharp
ipc.RegisterCommand("chart.read", command => {
    var chart = charts.Find((string)command.Payload["id"]);
    ipc.ReplyDownload(command, "chart.snapshot", chart.OpenDownload(),
        new { chart.Id, chart.FloorCount });
});
```

결과에는 `{url, byteLength, metadata}`와 원래 명령의 correlationId가 담깁니다.
메타데이터만 제어 메시지에 넣고 파일 본문은 다운로드 스트림으로 보냅니다.
ReplyDownload는 같은 namespace의 명령만 받으며 source 수명을 티켓에 넘깁니다.
결과를 전송하지 못하면 티켓을 폐기하고 source를 Dispose합니다.
송신 제어 메시지가 크기 제한을 넘으면 IpcMessageTooLargeException이 발생합니다.
명령 처리 중에는 `message_too_large` 코드의 correlated `command.failed`로 전달하고
연결과 다른 구독은 유지합니다. 실제 송신 큐 초과는 기존처럼 느린 소비자의 연결을 종료합니다.
