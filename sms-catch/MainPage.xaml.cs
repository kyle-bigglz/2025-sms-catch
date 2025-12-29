namespace sms_catch
{
    public partial class MainPage : ContentPage
    {
        private bool _isServiceRunning = false;
        private readonly List<string> _logs = new();

        public MainPage()
        {
            InitializeComponent();

#if ANDROID
            Loaded += OnPageLoaded;
            MainActivity.PermissionResultReceived += OnPermissionResultReceived;
            MainActivity.BatteryOptimizationResultReceived += OnBatteryOptimizationResultReceived;
            SmsReceiver.SmsReceived += OnSmsReceived;
#endif
        }

        private void OnPageLoaded(object? sender, EventArgs e)
        {
#if ANDROID
            CheckAndUpdatePermissionStatus();
            CheckAndUpdateBatteryStatus();
#endif
        }

#if ANDROID
        private void CheckAndUpdatePermissionStatus()
        {
            var activity = MainActivity.Instance;
            if (activity == null)
                return;

            var hasPermissions = activity.CheckPermissions();
            UpdatePermissionUI(hasPermissions);
        }

        private void UpdatePermissionUI(bool hasPermissions)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (hasPermissions)
                {
                    PermissionStatusLabel.Text = "권한 상태: 허용됨";
                    PermissionStatusLabel.TextColor = Colors.Green;
                    PermissionBtn.IsEnabled = false;
                    PermissionBtn.Text = "권한 허용됨";
                    StartServiceBtn.IsEnabled = true;
                }
                else
                {
                    PermissionStatusLabel.Text = "권한 상태: 필요";
                    PermissionStatusLabel.TextColor = Colors.Red;
                    PermissionBtn.IsEnabled = true;
                    StartServiceBtn.IsEnabled = false;
                }
            });
        }

        private void OnPermissionResultReceived(object? sender, bool allGranted)
        {
            UpdatePermissionUI(allGranted);

            if (allGranted)
            {
                AddLog("모든 권한이 허용되었습니다.");
            }
            else
            {
                AddLog("일부 권한이 거부되었습니다. 앱 설정에서 권한을 허용해주세요.");
            }
        }

        private void OnSmsReceived(object? sender, SmsReceivedEventArgs e)
        {
            AddLog($"SMS 수신: {e.Sender} - {e.Body.Substring(0, Math.Min(e.Body.Length, 30))}...");
        }

        private void CheckAndUpdateBatteryStatus()
        {
            var activity = MainActivity.Instance;
            if (activity == null)
                return;

            var isExempted = activity.IsIgnoringBatteryOptimizations();
            UpdateBatteryUI(isExempted);
        }

        private void UpdateBatteryUI(bool isExempted)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (isExempted)
                {
                    BatteryStatusLabel.Text = "배터리 최적화: 해제됨 ✓";
                    BatteryStatusLabel.TextColor = Colors.Green;
                    BatteryBtn.IsEnabled = false;
                    BatteryBtn.Text = "배터리 최적화 해제됨";
                    BatteryBtn.BackgroundColor = Colors.Gray;
                }
                else
                {
                    BatteryStatusLabel.Text = "배터리 최적화: 필요";
                    BatteryStatusLabel.TextColor = Colors.Orange;
                    BatteryBtn.IsEnabled = true;
                    BatteryBtn.Text = "배터리 최적화 해제";
                    BatteryBtn.BackgroundColor = Color.FromArgb("#FF9800");
                }
            });
        }

        private void OnBatteryOptimizationResultReceived(object? sender, bool isExempted)
        {
            UpdateBatteryUI(isExempted);

            if (isExempted)
            {
                AddLog("배터리 최적화에서 제외되었습니다.");
            }
            else
            {
                AddLog("배터리 최적화 해제가 거부되었습니다. 설정에서 직접 해제해주세요.");
            }
        }
#endif

        private void OnPermissionClicked(object? sender, EventArgs e)
        {
#if ANDROID
            var activity = MainActivity.Instance;
            if (activity == null)
            {
                AddLog("Activity를 찾을 수 없습니다.");
                return;
            }

            activity.RequestPermissions();
            AddLog("권한 요청 중...");
#else
            AddLog("이 기능은 안드로이드에서만 사용 가능합니다.");
#endif
        }

        private void OnBatteryOptimizationClicked(object? sender, EventArgs e)
        {
#if ANDROID
            var activity = MainActivity.Instance;
            if (activity == null)
            {
                AddLog("Activity를 찾을 수 없습니다.");
                return;
            }

            activity.RequestBatteryOptimizationExemption();
            AddLog("배터리 최적화 해제 요청 중...");
#else
            AddLog("이 기능은 안드로이드에서만 사용 가능합니다.");
#endif
        }

        private void OnStartServiceClicked(object? sender, EventArgs e)
        {
#if ANDROID
            var activity = MainActivity.Instance;
            if (activity == null)
            {
                AddLog("Activity를 찾을 수 없습니다.");
                return;
            }

            activity.StartSmsService();
            _isServiceRunning = true;
            UpdateServiceUI();
            AddLog("SMS 백업 서비스가 시작되었습니다.");
            AddLog("앱을 종료해도 백그라운드에서 계속 실행됩니다.");
#else
            AddLog("이 기능은 안드로이드에서만 사용 가능합니다.");
#endif
        }

        private void OnStopServiceClicked(object? sender, EventArgs e)
        {
#if ANDROID
            var activity = MainActivity.Instance;
            if (activity == null)
            {
                AddLog("Activity를 찾을 수 없습니다.");
                return;
            }

            activity.StopSmsService();
            _isServiceRunning = false;
            UpdateServiceUI();
            AddLog("SMS 백업 서비스가 중지되었습니다.");
#else
            AddLog("이 기능은 안드로이드에서만 사용 가능합니다.");
#endif
        }

        private void UpdateServiceUI()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_isServiceRunning)
                {
                    ServiceStatusLabel.Text = "서비스 상태: 실행 중 (백그라운드)";
                    ServiceStatusLabel.TextColor = Colors.Green;
                    StartServiceBtn.IsEnabled = false;
                    StopServiceBtn.IsEnabled = true;
                }
                else
                {
                    ServiceStatusLabel.Text = "서비스 상태: 중지됨";
                    ServiceStatusLabel.TextColor = Colors.Gray;
                    StartServiceBtn.IsEnabled = true;
                    StopServiceBtn.IsEnabled = false;
                }
            });
        }

        private void AddLog(string message)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                _logs.Add($"[{timestamp}] {message}");

                if (_logs.Count > 50)
                {
                    _logs.RemoveAt(0);
                }

                LogLabel.Text = string.Join("\n", _logs);
            });
        }
    }
}
