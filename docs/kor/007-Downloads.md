# 다운로드 스트리밍

AdofaiIpc 0.4.0부터 모드는 기존 localhost listener에 다운로드 메서드를 등록할 수 있습니다.
별도 서버나 브라우저의 `Blob` 생성이 필요하지 않습니다.

```csharp
ipc.RegisterDownload("recording.export", request =>
{
  // 호출자가 제공한 ID로 파일 경로를 직접 만들지 말고 내부 저장소에서 조회하세요.
  long byteLength = FindRecordingLength(request);
  return new IpcDownloadResponse(
    "audio/wav",
    byteLength,
    "recording.wav",
    destination => WriteRecordingTo(request, destination));
});
```

핸들러는 `IpcDownloadSource` 또는 `IpcDownloadError`를 반환합니다.
`IpcDownloadResponse`는 `IpcDownloadSource`의 편의 형식입니다. 콜백은 티켓을 사용한
HTTP 작업 스레드에서 실행되므로 Unity API를 직접 호출하지 않아야 합니다. 콜백은
전달받은 스트림을 닫지 않고 정확히 선언한 바이트 수만 기록해야 합니다. 스트림을
받는 생성자를 사용하면 AdofaiIpc가 다운로드가 끝나거나 티켓이 폐기될 때 닫습니다.

클라이언트는 기존 `POST /ipc` 호출로 메서드를 실행합니다. 성공 응답의 `result`는
`Url`과 `ByteLength`가 있는 작은 객체입니다. `Url`을 링크로 열면 브라우저가
`Content-Disposition: attachment` 응답을 기본 다운로드 기능으로 저장합니다.
오디오 전체를 JavaScript 메모리로 읽을 필요가 없습니다.

티켓은 60초 동안 유효하며 한 번만 사용할 수 있습니다. 티켓 수는 최대 128개,
동시 다운로드는 최대 4개입니다. namespace 등록 해제나 서버 종료 시 남은 티켓은
폐기됩니다. 허용된 Origin 정책은 티켓 발급에 적용되고, Origin이 있는 다운로드
요청에도 적용됩니다. 브라우저의 일반 다운로드 요청에는 Origin 헤더가 없을 수
있으므로 URL은 비밀로 취급하고 로그나 공유 페이지에 노출하지 마세요.
