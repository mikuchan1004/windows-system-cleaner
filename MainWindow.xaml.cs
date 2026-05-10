using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.ServiceProcess;
using System.Linq; // 추가: Any(), Contains() 사용을 위해 필요

namespace WindowsSystemCleaner
{
    public partial class MainWindow : Window
    {
        // 1. 변수 선언은 오직 이것 하나뿐이어야 합니다!
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

            var progress = new Progress<string>(msg => {
                LogText.Text += $"{msg}\n";
                Scroller.ScrollToBottom();
            });

            await Task.Run(() => StartCleaning(progress));

            string formattedSize = FormatSize(_totalSavedSize);

            LogText.Text += "\n" + new string('=', 30) + "\n";
            LogText.Text += $"    정리 완료: {formattedSize} 확보\n";
            LogText.Text += new string('=', 30) + "\n";

            StatusMsg.Text = $"최종 확보 용량: {formattedSize}";
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
            // 1. 사용자 임시 파일 (15%)
            CleanDirectory(Path.GetTempPath(), "사용자 임시 파일", progress);
            UpdateProgress(15);

            // 2. 시스템 임시 파일 (30%)
            CleanDirectory(@"C:\Windows\Temp", "시스템 임시 파일", progress);
            UpdateProgress(30);

            // 3. 프리패치 (45%)
            CleanDirectory(@"C:\Windows\Prefetch", "시스템 프리패치", progress);
            UpdateProgress(45);

            // 4. 업데이트 캐시 (60%)
            CleanUpdateCache(progress);
            UpdateProgress(60);

            // 5. 디스코드 캐시 (75%)
            string discordBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "discord");
            string[] discordTargets = ["Cache", "Code Cache", "GPUCache"];
            foreach (var target in discordTargets)
            {
                string fullPath = Path.Combine(discordBase, target);
                if (Directory.Exists(fullPath)) CleanDirectory(fullPath, $"디스코드 {target}", progress);
            }
            UpdateProgress(75);

            // 6. 프로젝트 자동 감지 및 정리 (100%)
            AutoCleanProjects(progress);
            UpdateProgress(100);
        }

        private void CleanDirectory(string path, string label, IProgress<string> progress)
        {
            progress.Report($"[작업] {label} 정리 중...");
            try
            {
                if (!Directory.Exists(path)) return;
                DirectoryInfo di = new(path);
                int count = 0;

                foreach (FileInfo file in di.GetFiles())
                {
                    try
                    {
                        long fileSize = file.Length;
                        file.Delete();
                        _totalSavedSize += fileSize;
                        count++;
                    }
                    catch { }
                }

                foreach (DirectoryInfo dir in di.GetDirectories())
                {
                    try
                    {
                        _totalSavedSize += GetDirectorySize(dir);
                        dir.Delete(true);
                        count++;
                    }
                    catch { }
                }
                progress.Report($" -> {count}개의 항목을 처리했습니다.");
            }
            catch (Exception ex) { progress.Report($" -> 오류: {ex.Message}"); }
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
                    foreach (var dir in root.GetDirectories("*", SearchOption.AllDirectories))
                    {
                        if (targets.Contains(dir.Name.ToLower()))
                        {
                            // [핵심 추가] 현재 작업 중인 프로젝트 폴더는 절대 건드리지 않음
                            // 이게 빠지면 bin/obj 안의 NuGet 참조 정보가 날아가서 에러가 터집니다.
                            if (dir.FullName.Contains("WindowsSystemCleaner"))
                            {
                                continue;
                            }

                            progress.Report($"[정리] {dir.FullName} 내부 소탕 중...");
                            CleanDirectoryContents(dir);
                        }
                    }
                }
                catch (Exception ex) { progress.Report($" -> 탐색 중 오류: {ex.Message}"); }
            }
        }

        private void CleanDirectoryContents(DirectoryInfo di)
        {
            // 1. 파일 삭제 시도
            foreach (FileInfo file in di.GetFiles())
            {
                try
                {
                    long size = file.Length;
                    file.Delete();
                    _totalSavedSize += size;
                }
                catch
                {
                    // 현재 VS가 사용 중인 파일(예: .suo, .db)은 여기서 자동으로 걸러짐
                }
            }

            // 2. 하위 폴더 삭제 시도
            foreach (DirectoryInfo subDir in di.GetDirectories())
            {
                try
                {
                    // 하위 폴더 내부를 먼저 비우고
                    CleanDirectoryContents(subDir);
                    // 비워진 폴더 삭제 (사용 중이면 삭제 안 됨)
                    subDir.Delete(true);
                }
                catch { }
            }
        }

        private static long GetDirectorySize(DirectoryInfo di)
        {
            long size = 0;
            try
            {
                foreach (FileInfo fi in di.GetFiles("*", SearchOption.AllDirectories)) size += fi.Length;
            }
            catch { }
            return size;
        }

        private void CleanUpdateCache(IProgress<string> progress)
        {
            progress.Report("[작업] Windows 업데이트 캐시 정리 중...");
            string path = @"C:\Windows\SoftwareDistribution\Download";

            // 1. 변수를 미리 선언해야 try/finally 어디서든 접근 가능합니다.
            ServiceController sc = null;

            try
            {
                sc = new ServiceController("wuauserv");
                progress.Report(" -> 업데이트 서비스 중지 중...");

                if (sc.Status == ServiceControllerStatus.Running)
                    sc.Stop();

                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));

                // 기존에 잘 작동하던 정리 로직 호출
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
                // 2. 여기서 sc를 안전하게 닫아줍니다.
                if (sc != null)
                {
                    sc.Close();
                    sc.Dispose();
                }
            }
        }

        private void UpdateProgress(int value)
        {
            Dispatcher.Invoke(() => {
                PBar.Value = value;
                StatusMsg.Text = $"진행률: {value}%";
            });
        }
        [System.Diagnostics.DebuggerNonUserCode]
        private void Window_Closed(object sender, EventArgs e)
        {
            // 디버거가 이 메서드 안에서 발생하는 일에 개입하지 못하게 차단
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }
}