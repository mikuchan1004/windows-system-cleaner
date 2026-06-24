using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.ServiceProcess;
using System.Linq;

namespace WindowsSystemCleaner
{
    public partial class MainWindow : Window
    {
        private long _totalSavedSize = 0;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            BtnStart.IsEnabled = false;
            LogText.Text = "시스템 분석 및 정리 시작...\n";
            _totalSavedSize = 0;
            PBar.Value = 0;
            StatusMsg.Text = "정리를 준비하는 중...";

            var progress = new Progress<string>(msg => {
                LogText.Text += $"{msg}\n";
                Scroller.ScrollToBottom();
            });

            await Task.Run(() => StartCleaning(progress));

            string formattedSize = FormatSize(_totalSavedSize);

            LogText.Text += "\n" + new string('=', 30) + "\n";
            LogText.Text += $"    정리 완료: {formattedSize} 확보\n";
            LogText.Text += new string('=', 30) + "\n";

            StatusMsg.Text = $"최종 확보 용량: {formattedSize} (작업 완료)";
            Scroller.ScrollToBottom();
            BtnStart.IsEnabled = true;
        }

        private static string FormatSize(long bytes)
        {
            string[] Suffix = ["B", "KB", "MB", "GB", "TB"];
            int i = 0;
            double dblSByte = bytes;
            while (dblSByte >= 1024 && i < Suffix.Length - 1)
            {
                dblSByte /= 1024;
                i++;
            }
            return $"{dblSByte:0.##} {Suffix[i]}";
        }

        private void StartCleaning(IProgress<string> progress)
        {
            // 1. 사용자 임시 파일 (10%)
            CleanDirectory(Path.GetTempPath(), "사용자 임시 파일", progress);
            UpdateProgress(10, "사용자 임시 파일 정리 완료");

            // 2. 시스템 임시 파일 (20%)
            CleanDirectory(@"C:\Windows\Temp", "시스템 임시 파일", progress);
            UpdateProgress(20, "시스템 임시 파일 정리 완료");

            // 3. 프리패치 (30%)
            CleanDirectory(@"C:\Windows\Prefetch", "시스템 프리패치", progress);
            UpdateProgress(30, "시스템 프리패치 정리 완료");

            // 4. 업데이트 캐시 (45%)
            CleanUpdateCache(progress);
            UpdateProgress(45, "Windows 업데이트 캐시 정리 완료");

            // 5. 디스코드 캐시 (55%)
            string discordBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "discord");
            string[] discordTargets = ["Cache", "Code Cache", "GPUCache"];
            foreach (var target in discordTargets)
            {
                string fullPath = Path.Combine(discordBase, target);
                if (Directory.Exists(fullPath)) CleanDirectory(fullPath, $"디스코드 {target}", progress);
            }
            UpdateProgress(55, "디스코드 캐시 소탕 완료");

            // 6. 프로젝트 자동 감지 및 정리 (80%)
            AutoCleanProjects(progress);
            UpdateProgress(80, "개발 프로젝트 찌꺼기 정리 완료");

            // 7. 시스템 고급 정리 (100%)
            AdvancedSystemCleanup(progress);
            UpdateProgress(100, "시스템 컴포넌트 최적화 완료");
        }

        // [핵심 전면 수정] SearchOption.AllDirectories 대신 안전한 재귀 탐색 구조 채택
        private void CleanDirectory(string path, string label, IProgress<string> progress)
        {
            progress.Report($"[작업] {label} 정리 중...");
            try
            {
                if (!Directory.Exists(path)) return;
                
                int count = 0;
                SafeCleanDirectoryRecursive(new DirectoryInfo(path), ref count);
                
                progress.Report($" -> {count}개의 항목을 처리했습니다.");
            }
            catch (Exception ex) { progress.Report($" -> 오류: {ex.Message}"); }
        }

        // 권한 거부, 점유 중 에러를 완벽히 격리하는 재귀 메서드
        private void SafeCleanDirectoryRecursive(DirectoryInfo di, ref int count)
        {
            // 1. 현재 폴더 안의 파일들 안전하게 삭제 시도
            try
            {
                foreach (FileInfo file in di.GetFiles())
                {
                    try
                    {
                        long fileSize = file.Length;
                        file.Delete(); 
                        _totalSavedSize += fileSize; 
                        count++;
                    }
                    catch { /* 사용 중이거나 엑세스 거부된 파일은 쿨하게 패스 */ }
                }
            }
            catch { /* 폴더 자체에 접근 권한이 없으면 통째로 패스 */ return; }

            // 2. 하위 폴더들을 하나씩 안전하게 탐색 (여기서 예외가 터져도 다른 하위 폴더는 계속 진행됨)
            try
            {
                foreach (DirectoryInfo subDir in di.GetDirectories())
                {
                    // 재귀 호출로 더 깊은 곳 소탕
                    SafeCleanDirectoryRecursive(subDir, ref count);

                    // 하위 폴더 내부가 비워졌다면 폴더 자체 삭제 시도
                    try
                    {
                        if (subDir.Exists) subDir.Delete(false); // 내부가 진짜 비었을 때만 삭제
                    }
                    catch { /* 지울 수 없는 상태면 패스 */ }
                }
            }
            catch { /* 하위 폴더 목록을 읽어오지 못하는 권한 에러 처리 */ }
        }

        private void AdvancedSystemCleanup(IProgress<string> progress)
        {
            string doPath = @"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache";
            if (Directory.Exists(doPath))
            {
                CleanDirectory(doPath, "배달 최적화 캐시", progress);
            }

            progress.Report("[고급] WinSxS 컴포넌트 스토어 최적화 중...");
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dism.exe",
                    Arguments = "/online /cleanup-image /startcomponentcleanup /resetbase",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas"
                };

                using var process = System.Diagnostics.Process.Start(psi);
                process?.WaitForExit();
                progress.Report(" -> 시스템 컴포넌트 최적화 완료.");
            }
            catch (Exception ex) { progress.Report($" -> DISM 실행 오류: {ex.Message}"); }
        }

        private void AutoCleanProjects(IProgress<string> progress)
        {
            string[] candidatePaths = [
                @"F:\Project",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Projects")
            ];

            string[] targets = [".vs", "bin", "obj"];

            foreach (var rootPath in candidatePaths)
            {
                if (!Directory.Exists(rootPath)) continue;

                try
                {
                    DirectoryInfo root = new(rootPath);
                    // 프로젝트 폴더 찾기는 최상위나 특정 깊이에서만 일어날 확률이 높지만, 
                    // 안전을 위해 여기서도 하위 디렉토리를 안전하게 열어봅니다.
                    CleanProjectDirectoriesSafe(root, targets, progress);
                }
                catch (Exception ex) { progress.Report($" -> 탐색 중 오류: {ex.Message}"); }
            }
        }

        // 프로젝트 탐색 시에도 권한 에러로 튕기는 것을 완벽 차단
        private void CleanProjectDirectoriesSafe(DirectoryInfo di, string[] targets, IProgress<string> progress)
        {
            try
            {
                foreach (var subDir in di.GetDirectories())
                {
                    if (targets.Contains(subDir.Name.ToLower()))
                    {
                        // 현재 작업 프로그램 보호
                        if (subDir.FullName.Contains("WindowsSystemCleaner")) continue;

                        progress.Report($"[정리] {subDir.FullName} 내부 소탕 중...");
                        CleanDirectoryContents(subDir);
                    }
                    else
                    {
                        // 대상 폴더가 아니면 하위로 더 내려가서 검색
                        CleanProjectDirectoriesSafe(subDir, targets, progress);
                    }
                }
            }
            catch { /* 접근 권한 없는 개발 폴더는 스킵 */ }
        }

        private void CleanDirectoryContents(DirectoryInfo di)
        {
            try
            {
                foreach (FileInfo file in di.GetFiles())
                {
                    try
                    {
                        long size = file.Length;
                        file.Delete();
                        _totalSavedSize += size;
                    }
                    catch { }
                }

                foreach (DirectoryInfo subDir in di.GetDirectories())
                {
                    try
                    {
                        CleanDirectoryContents(subDir);
                        subDir.Delete(true);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void CleanUpdateCache(IProgress<string> progress)
        {
            progress.Report("[작업] Windows 업데이트 캐시 정리 중...");
            string path = @"C:\Windows\SoftwareDistribution\Download";
            ServiceController sc = null;

            try
            {
                sc = new ServiceController("wuauserv");
                progress.Report(" -> 업데이트 서비스 중지 중...");

                if (sc.Status == ServiceControllerStatus.Running)
                    sc.Stop();

                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));

                CleanDirectory(path, "업데이트 다운로드 캐시", progress);

                progress.Report(" -> 업데이트 서비스 재시작 중...");
                sc.Start();
            }
            catch (Exception ex)
            {
                progress.Report($" -> 서비스 제어 오류: {ex.Message}");
            }
            finally
            {
                if (sc != null)
                {
                    sc.Close();
                    sc.Dispose();
                }
            }
        }

        // 상태창 메시지도 함께 업데이트하도록 개선
        private void UpdateProgress(int value, string statusText)
        {
            Dispatcher.Invoke(() => {
                PBar.Value = value;
                StatusMsg.Text = $"진행률: {value}% | {statusText}";
            });
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private void Window_Closed(object sender, EventArgs e)
        {
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }
}