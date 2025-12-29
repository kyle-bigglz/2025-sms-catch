using Android.Content;
using Android.Database;
using Android.Provider;
using Android.Util;
using AndroidUri = Android.Net.Uri;

namespace sms_catch;

public class SmsContentObserver : ContentObserver
{
    private const string Tag = "SmsContentObserver";
    private readonly Context _context;
    private long _lastSmsId = 0;

    public static event EventHandler<SmsReceivedEventArgs>? SmsReceived;

    public SmsContentObserver(Context context, Android.OS.Handler handler) : base(handler)
    {
        _context = context;
        _lastSmsId = GetLastSmsId();
        Log.Info(Tag, $"SmsContentObserver 초기화됨. 마지막 SMS ID: {_lastSmsId}");
    }

    public override void OnChange(bool selfChange)
    {
        OnChange(selfChange, null);
    }

    public override void OnChange(bool selfChange, AndroidUri? uri)
    {
        Log.Debug(Tag, $"OnChange 호출됨 - selfChange: {selfChange}, uri: {uri}");
        CheckForNewSms();
    }

    private long GetLastSmsId()
    {
        try
        {
            var cursor = _context.ContentResolver?.Query(
                Telephony.Sms.Inbox.ContentUri,
                new[] { Telephony.Sms.InterfaceConsts.Id },
                null,
                null,
                $"{Telephony.Sms.InterfaceConsts.Id} DESC");

            if (cursor != null && cursor.MoveToFirst())
            {
                var idIndex = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Id);
                if (idIndex >= 0)
                {
                    var id = cursor.GetLong(idIndex);
                    cursor.Close();
                    return id;
                }
                cursor.Close();
            }
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"GetLastSmsId 오류: {ex.Message}");
        }
        return 0;
    }

    private void CheckForNewSms()
    {
        try
        {
            var cursor = _context.ContentResolver?.Query(
                Telephony.Sms.Inbox.ContentUri,
                new[]
                {
                    Telephony.Sms.InterfaceConsts.Id,
                    Telephony.Sms.InterfaceConsts.Address,
                    Telephony.Sms.InterfaceConsts.Body,
                    Telephony.Sms.InterfaceConsts.Date
                },
                $"{Telephony.Sms.InterfaceConsts.Id} > ?",
                new[] { _lastSmsId.ToString() },
                $"{Telephony.Sms.InterfaceConsts.Id} ASC");

            if (cursor == null)
            {
                Log.Warn(Tag, "SMS 커서가 null입니다");
                return;
            }

            var count = cursor.Count;
            Log.Info(Tag, $"새 SMS 개수: {count}");

            while (cursor.MoveToNext())
            {
                var idIndex = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Id);
                var addressIndex = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Address);
                var bodyIndex = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Body);
                var dateIndex = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Date);

                if (idIndex < 0 || addressIndex < 0 || bodyIndex < 0 || dateIndex < 0)
                    continue;

                var id = cursor.GetLong(idIndex);
                var sender = cursor.GetString(addressIndex) ?? "Unknown";
                var body = cursor.GetString(bodyIndex) ?? "";
                var dateMillis = cursor.GetLong(dateIndex);
                var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(dateMillis).DateTime;

                Log.Info(Tag, $"새 SMS 발견: ID={id}, 발신자={sender}");

                _lastSmsId = id;

                // 이벤트 발생
                SmsReceived?.Invoke(this, new SmsReceivedEventArgs(sender, body, timestamp));

                // 백그라운드 서비스에 전달
                SmsBackgroundService.EnqueueSms(sender, body, timestamp);
            }

            cursor.Close();
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"CheckForNewSms 오류: {ex.Message}");
        }
    }
}
