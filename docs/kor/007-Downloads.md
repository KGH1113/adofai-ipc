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
