using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;

namespace sms_catch;

[BroadcastReceiver(
    Name = "com.companyname.smscatch.BootReceiver",
    Enabled = true,
    Exported = true,
    DirectBootAware = true)]
[IntentFilter(new[] {
    Intent.ActionBootCompleted,
    Intent.ActionLockedBootCompleted,
    "android.intent.action.QUICKBOOT_POWERON",
    "com.htc.intent.action.QUICKBOOT_POWERON"
})]
public class BootReceiver : BroadcastReceiver
{
    private const string Tag = "BootReceiver";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null) return;

        Log.Info(Tag, $"BootReceiver OnReceive: {intent?.Action}");

        try
        {
            var serviceIntent = new Intent(context, typeof(SmsBackgroundService));

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                Log.Info(Tag, "StartForegroundService 호출");
                context.StartForegroundService(serviceIntent);
            }
            else
            {
                Log.Info(Tag, "StartService 호출");
                context.StartService(serviceIntent);
            }

            Log.Info(Tag, "서비스 시작 완료");
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"서비스 시작 오류: {ex.Message}");
        }
    }
}
