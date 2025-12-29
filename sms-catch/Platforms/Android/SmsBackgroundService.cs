using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Util;
using AndroidX.Core.App;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using AndroidApp = Android.App.Application;

namespace sms_catch;

[Service(
    Name = "com.companyname.smscatch.SmsBackgroundService",
    Enabled = true,
    Exported = false,
    ForegroundServiceType = Android.Content.PM.ForegroundService.TypeDataSync)]
public class SmsBackgroundService : Service
{
    private const string Tag = "SmsBackgroundService";
    private const int NotificationId = 1001;
    private const string ChannelId = "sms_catch_channel";
    private const string ChannelName = "SMS Catch Service";

    private static readonly ConcurrentQueue<SmsData> _smsQueue = new();
    private static readonly HttpClient _httpClient = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isRunning;
    private PowerManager.WakeLock? _wakeLock;

    public static string ApiEndpoint { get; set; } = "https://nfc-core-dev.bigglz.com/api/bigglzNfc/smsDataSet";
    private const string ApiKey = "19e2ef5b-773d-4f5d-a203-2fc672b9c94f";

    public static void EnqueueSms(string sender, string body, DateTime timestamp)
    {
        Log.Info(Tag, $"EnqueueSms 호출됨: {sender}");
        _smsQueue.Enqueue(new SmsData(sender, body, timestamp));
        Log.Info(Tag, $"큐 크기: {_smsQueue.Count}");
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        Log.Info(Tag, "========== 서비스 OnCreate 호출됨 ==========");
        base.OnCreate();
        CreateNotificationChannel();
        Log.Info(Tag, "NotificationChannel 생성 완료");
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        Log.Info(Tag, "========== 서비스 OnStartCommand 호출됨 ==========");
        try
        {
            // Wake Lock 획득 (화면 꺼져도 CPU 유지)
            AcquireWakeLock();

            Log.Info(Tag, "알림 생성 시작");
            var notification = CreateNotification();
            Log.Info(Tag, "알림 생성 완료");

            Log.Info(Tag, "StartForeground 호출 시작");
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                StartForeground(NotificationId, notification, Android.Content.PM.ForegroundService.TypeDataSync);
            }
            else
            {
                StartForeground(NotificationId, notification);
            }
            Log.Info(Tag, "StartForeground 호출 완료");

            if (!_isRunning)
            {
                _isRunning = true;
                _cancellationTokenSource = new CancellationTokenSource();

                Log.Info(Tag, "백그라운드 태스크 시작");
                // SMS 큐 처리 태스크
                Task.Run(() => ProcessSmsQueueAsync(_cancellationTokenSource.Token));

                // SMS 폴링 태스크 (1초마다 새 SMS 확인)
                Task.Run(() => PollSmsAsync(_cancellationTokenSource.Token));
                Log.Info(Tag, "백그라운드 태스크 시작 완료");
            }

            return StartCommandResult.Sticky;
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"OnStartCommand 오류: {ex}");
            return StartCommandResult.NotSticky;
        }
    }

    private void AcquireWakeLock()
    {
        try
        {
            if (_wakeLock == null)
            {
                var powerManager = (PowerManager?)GetSystemService(PowerService);
                if (powerManager != null)
                {
                    _wakeLock = powerManager.NewWakeLock(WakeLockFlags.Partial, "SmsCatch::SmsBackgroundService");
                    _wakeLock.SetReferenceCounted(false);
                }
            }

            if (_wakeLock != null && !_wakeLock.IsHeld)
            {
                _wakeLock.Acquire();
                Log.Info(Tag, "Wake Lock 획득 완료");
            }
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"Wake Lock 획득 오류: {ex.Message}");
        }
    }

    private void ReleaseWakeLock()
    {
        try
        {
            if (_wakeLock != null && _wakeLock.IsHeld)
            {
                _wakeLock.Release();
                Log.Info(Tag, "Wake Lock 해제 완료");
            }
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"Wake Lock 해제 오류: {ex.Message}");
        }
    }

    private long _lastSmsId = 0;

    private async Task PollSmsAsync(CancellationToken cancellationToken)
    {
        Log.Info(Tag, "PollSmsAsync 태스크 시작됨");
        try
        {
            // 초기화: 마지막 SMS ID 가져오기
            _lastSmsId = GetLastSmsId();
            Log.Info(Tag, $"SMS 폴링 시작. 마지막 SMS ID: {_lastSmsId}");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    CheckForNewSms();
                    await Task.Delay(1000, cancellationToken); // 1초마다 확인 (테스트용)
                }
                catch (System.OperationCanceledException)
                {
                    Log.Info(Tag, "폴링 취소됨");
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(Tag, $"SMS 폴링 루프 오류: {ex.Message}\n{ex.StackTrace}");
                    await Task.Delay(1000, cancellationToken);
                }
            }
            Log.Info(Tag, "폴링 루프 종료");
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"PollSmsAsync 치명적 오류: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private long GetLastSmsId()
    {
        try
        {
            // 여러 Content Provider 시도 (Samsung RCS 포함)
            var providers = new[]
            {
                ("content://sms/inbox", "SMS Inbox"),
                ("content://sms", "SMS All"),
                ("content://mms-sms/conversations", "MMS-SMS Conversations"),
                ("content://mms-sms/complete-conversations", "Complete Conversations"),
                ("content://im/chat", "IM Chat (RCS)"),
                ("content://rcs/message", "RCS Message"),
                ("content://com.samsung.rcs.im/chat", "Samsung RCS Chat"),
                ("content://com.samsung.android.messaging", "Samsung Messaging"),
            };

            foreach (var (uri, name) in providers)
            {
                try
                {
                    Log.Info(Tag, $"========== {name} ({uri}) 조회 시작 ==========");
                    var smsUri = Android.Net.Uri.Parse(uri);
                    var cursor = ContentResolver?.Query(
                        smsUri,
                        null, // 모든 컬럼 가져오기
                        null,
                        null,
                        "date DESC");

                    if (cursor != null && cursor.Count > 0)
                    {
                        Log.Info(Tag, $"{name}: 총 {cursor.Count}개 레코드");

                        // 컬럼 이름 출력
                        var columnNames = cursor.GetColumnNames();
                        Log.Info(Tag, $"컬럼: {string.Join(", ", columnNames)}");

                        int count = 0;
                        while (cursor.MoveToNext() && count < 5)
                        {
                            count++;
                            var sb = new System.Text.StringBuilder();
                            sb.Append($"[{count}] ");

                            foreach (var col in new[] { "_id", "address", "body", "date", "type", "thread_id", "snippet", "normalized_date" })
                            {
                                var idx = cursor.GetColumnIndex(col);
                                if (idx >= 0)
                                {
                                    var val = cursor.GetString(idx) ?? "null";
                                    if (col == "date" || col == "normalized_date")
                                    {
                                        if (long.TryParse(val, out var ms) && ms > 0)
                                        {
                                            val = DateTimeOffset.FromUnixTimeMilliseconds(ms).DateTime.ToString("MM-dd HH:mm:ss");
                                        }
                                    }
                                    else if (col == "body" || col == "snippet")
                                    {
                                        val = val.Length > 15 ? val.Substring(0, 15) + "..." : val;
                                    }
                                    sb.Append($"{col}={val} | ");
                                }
                            }
                            Log.Info(Tag, sb.ToString());
                        }
                        cursor.Close();
                    }
                    else
                    {
                        Log.Warn(Tag, $"{name}: 데이터 없음 또는 접근 불가");
                        cursor?.Close();
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn(Tag, $"{name} 조회 실패: {ex.Message}");
                }
            }

            // Samsung RCS/IM 데이터베이스에서 마지막 받은 문자 ID 반환
            var mainUri = Android.Net.Uri.Parse("content://im/chat");
            var mainCursor = ContentResolver?.Query(
                mainUri,
                new[] { "_id", "type" },
                "type = ?",
                new[] { "1" },
                "_id DESC");

            if (mainCursor != null && mainCursor.MoveToFirst())
            {
                var idIndex = mainCursor.GetColumnIndex("_id");
                var lastId = mainCursor.GetLong(idIndex);
                mainCursor.Close();
                Log.Info(Tag, $"========== 마지막 받은 메시지 ID (IM/RCS): {lastId} ==========");
                return lastId;
            }
            mainCursor?.Close();
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"GetLastSmsId 오류: {ex.Message}\n{ex.StackTrace}");
        }
        return 0;
    }

    private static int _pollCount = 0;

    private void CheckForNewSms()
    {
        _pollCount++;

        try
        {
            // Samsung RCS/IM 데이터베이스 사용 (content://im/chat)
            var imUri = Android.Net.Uri.Parse("content://im/chat");

            // 10번마다 현재 DB의 최신 ID 로깅 (받은 문자만)
            if (_pollCount % 10 == 0)
            {
                var maxIdCursor = ContentResolver?.Query(
                    imUri,
                    new[] { "_id" },
                    "type = ?",
                    new[] { "1" },
                    "_id DESC");

                long currentMaxId = 0;
                if (maxIdCursor != null && maxIdCursor.MoveToFirst())
                {
                    var idx = maxIdCursor.GetColumnIndex("_id");
                    if (idx >= 0) currentMaxId = maxIdCursor.GetLong(idx);
                    maxIdCursor.Close();
                }

                Log.Info(Tag, $"IM/RCS 확인 중... (#{_pollCount}, 저장된 마지막 ID: {_lastSmsId}, DB 최신 ID: {currentMaxId})");
            }

            // 받은 문자만 검색 (type=1, _id > lastId)
            var cursor = ContentResolver?.Query(
                imUri,
                new[] { "_id", "address", "body", "date", "type" },
                "type = ? AND _id > ?",
                new[] { "1", _lastSmsId.ToString() },
                "_id ASC");

            if (cursor == null)
            {
                Log.Warn(Tag, "IM 커서가 null입니다");
                return;
            }

            var count = cursor.Count;
            if (count > 0)
            {
                Log.Info(Tag, $"새 메시지 발견: {count}개");
            }

            while (cursor.MoveToNext())
            {
                var idIndex = cursor.GetColumnIndex("_id");
                var addressIndex = cursor.GetColumnIndex("address");
                var bodyIndex = cursor.GetColumnIndex("body");
                var dateIndex = cursor.GetColumnIndex("date");
                var typeIndex = cursor.GetColumnIndex("type");

                if (idIndex < 0 || addressIndex < 0 || bodyIndex < 0 || dateIndex < 0)
                {
                    Log.Warn(Tag, $"컬럼 인덱스 오류: id={idIndex}, address={addressIndex}, body={bodyIndex}, date={dateIndex}");
                    continue;
                }

                var id = cursor.GetLong(idIndex);
                var sender = cursor.GetString(addressIndex) ?? "Unknown";
                var body = cursor.GetString(bodyIndex) ?? "";
                var dateMillis = cursor.GetLong(dateIndex);
                var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(dateMillis).DateTime;
                var smsType = typeIndex >= 0 ? cursor.GetInt(typeIndex) : -1;
                // type: 1=받은문자, 2=보낸문자

                Log.Info(Tag, $"새 메시지: ID={id}, 타입={smsType}, 발신자={sender}, 내용={body.Substring(0, Math.Min(body.Length, 20))}...");

                _lastSmsId = id;

                // 받은 문자만 큐에 추가 (type=1)
                if (smsType == 1)
                {
                    EnqueueSms(sender, body, timestamp);
                }
            }

            cursor.Close();
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"CheckForNewSms 오류: {ex.Message}");
        }
    }

    public override void OnDestroy()
    {
        Log.Info(Tag, "서비스 종료됨");
        _isRunning = false;
        _cancellationTokenSource?.Cancel();
        ReleaseWakeLock();
        base.OnDestroy();
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.Low)
            {
                Description = "SMS 백업 서비스가 실행 중입니다."
            };

            var notificationManager = (NotificationManager?)GetSystemService(NotificationService);
            notificationManager?.CreateNotificationChannel(channel);
        }
    }

    private Notification CreateNotification()
    {
        var pendingIntentFlags = PendingIntentFlags.UpdateCurrent;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
        {
            pendingIntentFlags |= PendingIntentFlags.Immutable;
        }

        var intent = new Intent(this, typeof(MainActivity));
        var pendingIntent = PendingIntent.GetActivity(this, 0, intent, pendingIntentFlags);

        return new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("SMS 백업 서비스")
            .SetContentText("SMS 백업 서비스가 실행 중입니다.")
            .SetSmallIcon(Resource.Drawable.notification_icon_background)
            .SetContentIntent(pendingIntent)
            .SetOngoing(true)
            .Build();
    }

    private async Task ProcessSmsQueueAsync(CancellationToken cancellationToken)
    {
        Log.Info(Tag, "ProcessSmsQueueAsync 시작됨");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                while (_smsQueue.TryDequeue(out var smsData))
                {
                    Log.Info(Tag, $"큐에서 SMS 처리 중: {smsData.Sender}");
                    await SendSmsToApiAsync(smsData);

                    // API 호출 사이에 짧은 딜레이 (서버 부하 방지)
                    await Task.Delay(500, cancellationToken);
                }

                await Task.Delay(1000, cancellationToken);
            }
            catch (System.OperationCanceledException)
            {
                Log.Info(Tag, "ProcessSmsQueueAsync 취소됨");
                break;
            }
            catch (Exception ex)
            {
                Log.Error(Tag, $"SMS 처리 오류: {ex.Message}");
            }
        }
    }

    private async Task SendSmsToApiAsync(SmsData smsData)
    {
        Log.Info(Tag, $"API 전송 시작: {smsData.Sender} -> {ApiEndpoint}");
        try
        {
            var payload = new
            {
                key = ApiKey,
                sender = smsData.Sender,
                body = smsData.Body,
                sendTime = smsData.Timestamp // DateTime 객체 직접 전달
            };

            // API 응답이 느릴 수 있으므로 타임아웃 설정 (60초)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var response = await _httpClient.PostAsJsonAsync(ApiEndpoint, payload, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                Log.Info(Tag, $"SMS 전송 성공: {smsData.Sender}, 응답: {responseBody}");
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Log.Warn(Tag, $"SMS 전송 실패: {response.StatusCode}, 응답: {errorBody}");
                _smsQueue.Enqueue(smsData);
            }
        }
        catch (System.OperationCanceledException)
        {
            Log.Warn(Tag, $"API 전송 타임아웃: {smsData.Sender}");
            _smsQueue.Enqueue(smsData);
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"API 전송 오류: {ex.Message}");
            _smsQueue.Enqueue(smsData);
        }
    }
}

public record SmsData(string Sender, string Body, DateTime Timestamp);
