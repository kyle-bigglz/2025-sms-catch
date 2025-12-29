using Android.App;
using Android.Content;
using Android.Provider;
using Android.Util;
using AndroidSmsMessage = Android.Telephony.SmsMessage;

namespace sms_catch;

[BroadcastReceiver(
    Name = "com.companyname.smscatch.SmsReceiver",
    Enabled = true,
    Exported = true,
    Permission = "android.permission.BROADCAST_SMS")]
[IntentFilter(new[] { Telephony.Sms.Intents.SmsReceivedAction }, Priority = 999)]
public class SmsReceiver : BroadcastReceiver
{
    private const string Tag = "SmsReceiver";
    public static event EventHandler<SmsReceivedEventArgs>? SmsReceived;

    public override void OnReceive(Context? context, Intent? intent)
    {
        Log.Debug(Tag, $"OnReceive 호출됨 - Action: {intent?.Action}");

        if (intent?.Action != Telephony.Sms.Intents.SmsReceivedAction)
        {
            Log.Debug(Tag, "Action이 SMS_RECEIVED가 아님");
            return;
        }

        var bundle = intent.Extras;
        if (bundle == null)
            return;

        var pdus = bundle.Get("pdus");
        if (pdus == null)
            return;

        var pdusArray = (Java.Lang.Object[]?)pdus;
        if (pdusArray == null)
            return;

        var format = bundle.GetString("format") ?? "3gpp";

        foreach (var pdu in pdusArray)
        {
            var bytes = (byte[]?)pdu;
            if (bytes == null)
                continue;

            var message = AndroidSmsMessage.CreateFromPdu(bytes, format);
            if (message == null)
                continue;

            var sender = message.OriginatingAddress ?? "Unknown";
            var body = message.MessageBody ?? "";
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(message.TimestampMillis).DateTime;

            Log.Info(Tag, $"SMS 수신: {sender} - {body.Substring(0, Math.Min(body.Length, 20))}...");

            // 이벤트 발생
            SmsReceived?.Invoke(null, new SmsReceivedEventArgs(sender, body, timestamp));

            // 백그라운드 서비스에 전달
            SmsBackgroundService.EnqueueSms(sender, body, timestamp);
            Log.Info(Tag, "SMS를 큐에 추가함");
        }
    }
}

public class SmsReceivedEventArgs : EventArgs
{
    public string Sender { get; }
    public string Body { get; }
    public DateTime Timestamp { get; }

    public SmsReceivedEventArgs(string sender, string body, DateTime timestamp)
    {
        Sender = sender;
        Body = body;
        Timestamp = timestamp;
    }
}
