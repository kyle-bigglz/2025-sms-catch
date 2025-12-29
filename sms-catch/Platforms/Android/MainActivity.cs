using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using AndroidProvider = Android.Provider;

namespace sms_catch
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        private const int PermissionRequestCode = 1000;
        private const int BatteryOptimizationRequestCode = 1001;

        public static MainActivity? Instance { get; private set; }

        public static readonly string[] RequiredPermissions = new[]
        {
            Manifest.Permission.ReceiveSms,
            Manifest.Permission.ReadSms,
            Manifest.Permission.PostNotifications
        };

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Instance = this;
        }

        public bool CheckPermissions()
        {
            foreach (var permission in RequiredPermissions)
            {
                if (ContextCompat.CheckSelfPermission(this, permission) != Permission.Granted)
                {
                    return false;
                }
            }
            return true;
        }

        public void RequestPermissions()
        {
            var permissionsToRequest = RequiredPermissions
                .Where(p => ContextCompat.CheckSelfPermission(this, p) != Permission.Granted)
                .ToArray();

            if (permissionsToRequest.Length > 0)
            {
                ActivityCompat.RequestPermissions(this, permissionsToRequest, PermissionRequestCode);
            }
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            if (requestCode == PermissionRequestCode)
            {
                var allGranted = grantResults.All(r => r == Permission.Granted);
                PermissionResultReceived?.Invoke(this, allGranted);
            }
        }

        public static event EventHandler<bool>? PermissionResultReceived;

        public void StartSmsService()
        {
            Log.Info("MainActivity", "StartSmsService 호출됨");
            try
            {
                var intent = new Intent(this, typeof(SmsBackgroundService));
                Log.Info("MainActivity", $"Intent 생성됨: {intent}");

                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    Log.Info("MainActivity", "StartForegroundService 호출 시도");
                    StartForegroundService(intent);
                    Log.Info("MainActivity", "StartForegroundService 호출 완료");
                }
                else
                {
                    Log.Info("MainActivity", "StartService 호출 시도");
                    StartService(intent);
                    Log.Info("MainActivity", "StartService 호출 완료");
                }
            }
            catch (Exception ex)
            {
                Log.Error("MainActivity", $"서비스 시작 오류: {ex}");
            }
        }

        public void StopSmsService()
        {
            var intent = new Intent(this, typeof(SmsBackgroundService));
            StopService(intent);
        }

        public bool IsIgnoringBatteryOptimizations()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            {
                var powerManager = (PowerManager?)GetSystemService(PowerService);
                return powerManager?.IsIgnoringBatteryOptimizations(PackageName!) ?? false;
            }
            return true;
        }

        public void RequestBatteryOptimizationExemption()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            {
                if (!IsIgnoringBatteryOptimizations())
                {
                    try
                    {
                        Log.Info("MainActivity", "배터리 최적화 해제 요청");
                        var intent = new Intent(AndroidProvider.Settings.ActionRequestIgnoreBatteryOptimizations);
                        intent.SetData(Android.Net.Uri.Parse($"package:{PackageName}"));
                        StartActivityForResult(intent, BatteryOptimizationRequestCode);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("MainActivity", $"배터리 최적화 해제 요청 오류: {ex.Message}");
                        // 일부 기기에서 ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS가 지원되지 않을 수 있음
                        // 대신 배터리 설정 화면으로 이동
                        try
                        {
                            var settingsIntent = new Intent(AndroidProvider.Settings.ActionIgnoreBatteryOptimizationSettings);
                            StartActivity(settingsIntent);
                        }
                        catch
                        {
                            Log.Error("MainActivity", "배터리 설정 화면 열기 실패");
                        }
                    }
                }
                else
                {
                    Log.Info("MainActivity", "이미 배터리 최적화에서 제외됨");
                }
            }
        }

        public static event EventHandler<bool>? BatteryOptimizationResultReceived;

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if (requestCode == BatteryOptimizationRequestCode)
            {
                var isExempted = IsIgnoringBatteryOptimizations();
                Log.Info("MainActivity", $"배터리 최적화 해제 결과: {isExempted}");
                BatteryOptimizationResultReceived?.Invoke(this, isExempted);
            }
        }
    }
}
