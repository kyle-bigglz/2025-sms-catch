# SMS Catch 기술 인수인계서

## 1. 프로젝트 개요

| 항목 | 내용 |
|------|------|
| 프로젝트명 | SMS Catch (sms-catch) |
| 프레임워크 | .NET 10 MAUI |
| 대상 플랫폼 | Android 전용 (API 21+) |
| 패키지명 | `com.companyname.smscatch` |
| 주요 기능 | SMS 수신 감지 및 외부 API 전송 |

---

## 2. 시스템 아키텍처

```
┌─────────────────────────────────────────────────────────────────┐
│                        SMS Catch App                            │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────┐    ┌──────────────┐    ┌──────────────────┐   │
│  │  MainPage   │───▶│ MainActivity │───▶│ SmsBackground    │   │
│  │   (UI)      │    │  (권한관리)   │    │    Service       │   │
│  └─────────────┘    └──────────────┘    └────────┬─────────┘   │
│                                                   │             │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │                   SMS 수신 감지 계층                      │   │
│  ├─────────────┬─────────────────┬─────────────────────────┤   │
│  │ SmsReceiver │ SmsContent      │ 폴링 (1초 간격)          │   │
│  │(Broadcast)  │   Observer      │ Samsung RCS/IM 지원      │   │
│  └──────┬──────┴────────┬────────┴────────────┬────────────┘   │
│         │               │                      │                │
│         └───────────────┴──────────────────────┘                │
│                          │                                      │
│                    ┌─────▼─────┐                                │
│                    │ SMS Queue │  (ConcurrentQueue)             │
│                    └─────┬─────┘                                │
│                          │                                      │
│                    ┌─────▼─────┐                                │
│                    │ HTTP API  │                                │
│                    │  전송     │                                │
│                    └───────────┘                                │
└─────────────────────────────────────────────────────────────────┘
                           │
                           ▼
              ┌────────────────────────┐
              │   외부 API 서버        │
              │ nfc-core-dev.bigglz.com│
              └────────────────────────┘
```

---

## 3. SMS 수신 감지 기술

### 3.1 수신 감지 방식 (3중 구조)

앱은 다양한 환경에서 SMS를 안정적으로 감지하기 위해 **3가지 방식**을 동시에 사용합니다:

#### 방식 1: BroadcastReceiver (SmsReceiver.cs)

| 항목 | 내용 |
|------|------|
| 파일 | `Platforms/Android/SmsReceiver.cs` |
| 클래스 | `SmsReceiver : BroadcastReceiver` |
| 동작 원리 | `android.provider.Telephony.SMS_RECEIVED` 브로드캐스트 수신 |
| 우선순위 | 999 (높음) |
| 장점 | 실시간 감지, 배터리 효율적 |
| 단점 | 일부 기기/Samsung RCS 미지원 |

```csharp
[IntentFilter(new[] { Telephony.Sms.Intents.SmsReceivedAction }, Priority = 999)]
public class SmsReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        // PDU 파싱 → SMS 추출 → 큐에 추가
    }
}
```

#### 방식 2: ContentObserver (SmsContentObserver.cs)

| 항목 | 내용 |
|------|------|
| 파일 | `Platforms/Android/SmsContentObserver.cs` |
| 클래스 | `SmsContentObserver : ContentObserver` |
| 동작 원리 | `content://sms/inbox` 데이터베이스 변경 감시 |
| 장점 | SMS 저장소 변경 감지 |
| 단점 | 변경 알림 지연 가능 |

#### 방식 3: 폴링 (SmsBackgroundService.cs)

| 항목 | 내용 |
|------|------|
| 파일 | `Platforms/Android/SmsBackgroundService.cs` |
| 메서드 | `PollSmsAsync()`, `CheckForNewSms()` |
| 동작 원리 | 1초마다 `content://im/chat` 쿼리 |
| 대상 | Samsung RCS/IM 메시지 |
| 장점 | Samsung 기기 RCS 메시지 지원 |
| 단점 | 배터리 소모 증가 |

**지원하는 Content Provider URI:**
```
content://sms/inbox           - 표준 SMS
content://sms                 - 전체 SMS
content://mms-sms/conversations
content://im/chat             - Samsung RCS/IM (주 사용)
content://rcs/message         - RCS
content://com.samsung.rcs.im/chat
```

---

## 4. 백그라운드 서비스 구조

### 4.1 Foreground Service (SmsBackgroundService.cs)

앱 종료 후에도 SMS 감지를 유지하기 위해 **Android Foreground Service**를 사용합니다.

| 항목 | 내용 |
|------|------|
| 서비스 타입 | `ForegroundService.TypeDataSync` |
| 알림 채널 | `sms_catch_channel` |
| 알림 ID | 1001 |
| 시작 방식 | `StartCommandResult.Sticky` (시스템 종료 시 재시작) |

```csharp
[Service(
    Name = "com.companyname.smscatch.SmsBackgroundService",
    ForegroundServiceType = Android.Content.PM.ForegroundService.TypeDataSync)]
public class SmsBackgroundService : Service
```

### 4.2 Wake Lock

화면이 꺼진 상태에서도 CPU가 작동하도록 **Partial Wake Lock**을 사용합니다.

```csharp
_wakeLock = powerManager.NewWakeLock(WakeLockFlags.Partial, "SmsCatch::SmsBackgroundService");
```

### 4.3 부팅 시 자동 시작 (BootReceiver.cs)

| 항목 | 내용 |
|------|------|
| 파일 | `Platforms/Android/BootReceiver.cs` |
| 지원 이벤트 | `BOOT_COMPLETED`, `LOCKED_BOOT_COMPLETED`, `QUICKBOOT_POWERON` |
| Direct Boot | 지원 (`DirectBootAware = true`) |

---

## 5. API 전송 구조

### 5.1 전송 대상

| 항목 | 내용 |
|------|------|
| 엔드포인트 | `https://nfc-core-dev.bigglz.com/api/bigglzNfc/smsDataSet` |
| API Key | `19e2ef5b-773d-4f5d-a203-2fc672b9c94f` |
| 메서드 | POST (JSON) |
| 타임아웃 | 60초 |

### 5.2 전송 데이터 구조

```json
{
    "key": "19e2ef5b-773d-4f5d-a203-2fc672b9c94f",
    "sender": "+821012345678",
    "body": "문자 내용",
    "sendTime": "2024-12-16T14:30:00"
}
```

### 5.3 전송 흐름

```
SMS 수신 → ConcurrentQueue 추가 → ProcessSmsQueueAsync → HTTP POST
                    │
                    │ (실패 시)
                    └─────────────→ 큐에 재추가 (재시도)
```

### 5.4 재시도 로직

- API 호출 실패 시 → 큐에 다시 추가
- 타임아웃 발생 시 → 큐에 다시 추가
- API 호출 간격: 500ms

---

## 6. 권한 요구사항

### 6.1 AndroidManifest.xml 권한

| 권한 | 용도 |
|------|------|
| `RECEIVE_SMS` | SMS 수신 브로드캐스트 |
| `READ_SMS` | SMS 데이터베이스 읽기 |
| `INTERNET` | API 전송 |
| `ACCESS_NETWORK_STATE` | 네트워크 상태 확인 |
| `FOREGROUND_SERVICE` | 백그라운드 서비스 |
| `FOREGROUND_SERVICE_DATA_SYNC` | 데이터 동기화 서비스 타입 |
| `POST_NOTIFICATIONS` | 서비스 알림 표시 |
| `RECEIVE_BOOT_COMPLETED` | 부팅 시 자동 시작 |
| `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS` | 배터리 최적화 예외 요청 |
| `WAKE_LOCK` | CPU 유지 |

### 6.2 런타임 권한 요청

MainActivity에서 다음 권한을 런타임에 요청:
- `RECEIVE_SMS`
- `READ_SMS`
- `POST_NOTIFICATIONS`

---

## 7. 파일 구조

```
sms-catch/
├── App.xaml / App.xaml.cs          # 앱 진입점
├── AppShell.xaml / AppShell.xaml.cs
├── MainPage.xaml / MainPage.xaml.cs # UI 및 이벤트 처리
├── MauiProgram.cs                   # MAUI 설정
├── sms-catch.csproj                 # 프로젝트 설정
│
└── Platforms/
    └── Android/
        ├── AndroidManifest.xml      # 권한 및 컴포넌트 정의
        ├── MainActivity.cs          # 메인 Activity, 권한 관리
        ├── MainApplication.cs       # Application 클래스
        ├── SmsReceiver.cs           # BroadcastReceiver (SMS 수신)
        ├── SmsContentObserver.cs    # ContentObserver (DB 감시)
        ├── SmsBackgroundService.cs  # Foreground Service (핵심)
        └── BootReceiver.cs          # 부팅 시 자동 시작
```

---

## 8. 핵심 클래스 설명

### 8.1 SmsBackgroundService

| 메서드 | 역할 |
|--------|------|
| `OnStartCommand()` | 서비스 시작, Foreground 전환, 태스크 시작 |
| `PollSmsAsync()` | 1초 간격 SMS 폴링 |
| `CheckForNewSms()` | 새 SMS 확인 및 큐 추가 |
| `ProcessSmsQueueAsync()` | 큐에서 SMS 추출 후 API 전송 |
| `SendSmsToApiAsync()` | HTTP POST 전송 |
| `EnqueueSms()` | 정적 메서드, SMS 큐 추가 |

### 8.2 SmsReceiver

| 메서드 | 역할 |
|--------|------|
| `OnReceive()` | SMS 브로드캐스트 수신, PDU 파싱 |

### 8.3 MainActivity

| 메서드 | 역할 |
|--------|------|
| `CheckPermissions()` | 권한 확인 |
| `RequestPermissions()` | 권한 요청 |
| `StartSmsService()` | 백그라운드 서비스 시작 |
| `StopSmsService()` | 서비스 중지 |
| `RequestBatteryOptimizationExemption()` | 배터리 최적화 예외 요청 |

---

## 9. 빌드 및 배포

### 9.1 빌드 환경

- .NET SDK 10.0
- Visual Studio 2022 (17.12+) 또는 VS Code + .NET MAUI Extension
- Android SDK (API 21+)

### 9.2 빌드 명령

```bash
# Debug 빌드
dotnet build -f net10.0-android

# Release 빌드
dotnet publish -f net10.0-android -c Release
```

### 9.3 APK 위치

```
bin/Release/net10.0-android/publish/com.companyname.smscatch-Signed.apk
```

---

## 10. 주의사항 및 제한사항

### 10.1 Samsung 기기 특이사항

- Samsung 기기는 RCS(Rich Communication Services)를 기본 사용
- 표준 `content://sms` 대신 `content://im/chat` 사용 필요
- 일부 Samsung 기기에서 BroadcastReceiver가 작동하지 않을 수 있음

### 10.2 배터리 최적화

- 앱이 Doze 모드에서 종료될 수 있음
- **배터리 최적화 예외** 설정 필수
- UI에서 "배터리 최적화 해제" 버튼 제공

### 10.3 Android 버전별 제한

| Android 버전 | 제한사항 |
|-------------|---------|
| Android 8.0+ | Foreground Service 필수 |
| Android 10+ | 백그라운드 위치 접근 제한 |
| Android 13+ | POST_NOTIFICATIONS 런타임 권한 필요 |

---

## 11. 문제 해결 가이드

### 11.1 SMS가 감지되지 않을 때

1. 권한 확인 (RECEIVE_SMS, READ_SMS)
2. 배터리 최적화 해제 확인
3. 기본 SMS 앱 확인 (Samsung Messages vs Google Messages)
4. Logcat에서 `SmsReceiver`, `SmsBackgroundService` 태그 확인

### 11.2 서비스가 종료될 때

1. 배터리 최적화 예외 설정
2. 기기별 자동 시작 설정 확인 (MIUI, OneUI 등)
3. Wake Lock 상태 확인

### 11.3 API 전송 실패

1. 네트워크 연결 상태 확인
2. API 엔드포인트 응답 확인
3. Logcat에서 `API 전송` 관련 로그 확인

---

## 12. 로그 태그 목록

| 태그 | 용도 |
|------|------|
| `MainActivity` | 액티비티 라이프사이클, 권한 |
| `SmsReceiver` | 브로드캐스트 수신 |
| `SmsContentObserver` | ContentObserver 이벤트 |
| `SmsBackgroundService` | 서비스 상태, API 전송 |
| `BootReceiver` | 부팅 시 시작 |

```bash
# Logcat 필터링 예시
adb logcat -s MainActivity:* SmsReceiver:* SmsBackgroundService:* BootReceiver:*
```

---

## 13. 변경 이력

| 날짜 | 내용 |
|------|------|
| 2024-12-16 | 초기 개발 |
| 2024-12-29 | 기술 인수인계서 작성 |

---

*작성일: 2024-12-29*
