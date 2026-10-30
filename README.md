# HAR Recorder

Windows용 GUI HAR 캡처 도구. **C# / .NET 8 Windows Forms** 기반이며, 네트워크 캡처 엔진으로 **mitmproxy (`mitmdump`)**를 사용합니다.

> **실행 파일은 독립형 .NET EXE이지만, 트래픽 수집에는 별도의 `mitmdump.exe`가 필요합니다.** MITM 프록시를 시작하지 않고 Chrome에 `--proxy-server=127.0.0.1:8080`만 적용하면 인터넷이 연결되지 않을 수 있습니다.

## 작동 구조

```text
[Start Recording]
       |
       v
[mitmdump: 127.0.0.1:8080]
       |
       v
[Chrome: isolated profile + proxy]
       |
       v
[HTTP(S) requests] --> [temporary .mitm flow]
                              |
                       [Stop and Save]
                              |
                              v
                [mitmdump -n -r ... --set hardump=...]
                              |
                              v
                         [HAR file]
```

## 요구 사항

- Windows 10/11 (x64), Google Chrome
- 소스 빌드: **.NET 8 SDK**
- HAR 수집: **mitmproxy / mitmdump** (미설치 시 `winget` 자동 설치 시도)
- HTTPS 복호화를 원하는 경우, 별도로 **mitmproxy CA 인증서** 설치 필요

## 실행 파일 빌드

1. 이 저장소를 복제하거나 ZIP으로 다운로드합니다.
2. Windows에서 `BUILD_FINAL_ONE_EXE.bat`을 실행합니다. PowerShell에서는 `BUILD_FINAL_ONE_EXE.ps1`도 사용할 수 있습니다.
3. 정상 빌드 후 `_FINAL\HarRecorder.exe`를 실행합니다.
4. GUI에서 **Start Recording + Open Chrome**을 누릅니다.
5. Chrome에서 재현할 화면을 조작합니다.
6. **Stop and Save HAR**를 누르고 저장 위치를 선택합니다.

빌드 명령:

```powershell
dotnet publish HarRecorder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

## HTTPS 캡처

HTTPS 본문을 캡처하려면 녹화를 한 번 시작한 뒤 **Install HTTPS Cert**를 사용하거나 프록시 실행 중 `http://mitm.it`을 엽니다.

**주의:** 이 기능은 현재 사용자 계정의 신뢰 루트 저장소에 mitmproxy CA 인증서를 추가합니다. 신뢰 루트 인증서는 이 PC의 HTTPS 트래픽에 영향을 줄 수 있습니다. 공유 PC 및 타인의 트래픽에는 사용하지 마세요. 테스트 완료 후 필요하지 않은 인증서는 Windows 인증서 관리자에서 제거하세요.

## 저장 데이터와 공개 범위

- HAR에는 **쿠키, 인증 토큰, URL, 쿼리, 요청/응답 본문, 개인 정보**가 들어갈 수 있습니다.
- 이 저장소에는 실제 캡처 파일, 인증서, 사용자 프로필, 빌드 산출물을 포함하지 않습니다.
- 수집한 HAR를 GitHub Issues/PR이나 공개 저장소에 업로드하지 마세요.
- Chrome 프로필은 `C:\temp\chrome-har-profile`에 남습니다. 기록이 끝나고 필요 없으면 Chrome을 종료한 후 직접 삭제하세요.
- 기본 포트는 `127.0.0.1:8080`이며, GUI에서 변경할 수 있습니다.
- `C:\har-capture` 폴더는 생성하지만 **기본 자동 저장 폴더로 지정되지 않습니다.** 저장 경로는 Save 대화상자에서 선택합니다.

## 점검 시 확인된 제한 사항

- `Stop` 시 `mitmdump` 프로세스가 강제 종료될 수 있습니다. 종료 직전 요청의 일부가 누락되거나 `.mitm` 파일이 불완전할 가능성이 있습니다.
- `winget`으로 mitmproxy 설치 후 새 PATH를 현재 프로세스에서 바로 인식하지 못할 수 있습니다. 이 경우 앱을 재시작하세요.
- 프록시 포트가 이미 사용 중이거나 HTTPS 인증서를 설치하지 않으면 캡처/사이트 접속이 실패할 수 있습니다.
- Windows에서 자동 빌드 및 실제 HAR 생성 테스트는 별도로 확인해야 합니다.

## GitHub Actions

`Actions`의 **Build Windows standalone EXE** 작업을 실행하면 `HarRecorder-windows-x64` 빌드 아티팩트를 받을 수 있습니다.

## License

현재 별도 라이선스가 지정되지 않았습니다. 공개 저장소라는 사실만으로 제3자에게 재배포 및 수정 권한이 부여되는 것은 아닙니다.
