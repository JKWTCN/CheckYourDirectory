using Newtonsoft.Json;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Formatting = Newtonsoft.Json.Formatting;

namespace CheckYourDirectory
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择文件夹",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                FolderPathTextBox.Text = dialog.FolderName;
            }
        }

        private async void GenerateChecksum_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FolderPathTextBox.Text))
            {
                MessageBox.Show("请先选择文件夹", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(FolderPathTextBox.Text))
            {
                MessageBox.Show("文件夹不存在", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var algorithm = ((ComboBoxItem)HashAlgorithmComboBox.SelectedItem).Content.ToString();
            var folderPath = FolderPathTextBox.Text;
            var outputFile = Path.Combine(folderPath, "folder_checksum.json");

            try
            {
                ProgressBar.IsIndeterminate = true;
                LogTextBox.Text = "正在计算文件校验值...\n";

                var checksumData = await Task.Run(() => CalculateFolderChecksums(folderPath, algorithm));

                var json = JsonConvert.SerializeObject(checksumData, Formatting.Indented);
                File.WriteAllText(outputFile, json);

                LogTextBox.AppendText($"校验文件已生成: {outputFile}\n");
                LogTextBox.AppendText($"共处理 {checksumData.Count} 个文件\n");
            }
            catch (Exception ex)
            {
                LogTextBox.AppendText($"错误: {ex.Message}\n");
            }
            finally
            {
                ProgressBar.IsIndeterminate = false;
            }
        }

        private async void CompareFolder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FolderPathTextBox.Text))
            {
                MessageBox.Show("请先选择文件夹", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var folderPath = FolderPathTextBox.Text;
            var checksumFile = Path.Combine(folderPath, "folder_checksum.json");

            if (!File.Exists(checksumFile))
            {
                MessageBox.Show("找不到校验文件 (folder_checksum.json)", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                ProgressBar.IsIndeterminate = true;
                LogTextBox.Text = "正在读取校验文件...\n";

                var algorithm = ((ComboBoxItem)HashAlgorithmComboBox.SelectedItem).Content.ToString();
                var originalData = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(checksumFile));

                LogTextBox.AppendText("正在比对文件夹内容...\n");

                var currentData = await Task.Run(() => CalculateFolderChecksums(folderPath, algorithm));

                // 比较结果
                var comparison = CompareChecksums(originalData, currentData);

                LogTextBox.AppendText("\n=== 比对结果 ===\n");
                LogTextBox.AppendText($"新增文件: {comparison.AddedFiles.Count}\n");
                LogTextBox.AppendText($"删除文件: {comparison.DeletedFiles.Count}\n");
                LogTextBox.AppendText($"修改文件: {comparison.ModifiedFiles.Count}\n");

                if (comparison.AddedFiles.Count > 0)
                {
                    LogTextBox.AppendText("\n新增文件:\n");
                    foreach (var file in comparison.AddedFiles)
                    {
                        LogTextBox.AppendText($"+ {file}\n");
                    }
                }

                if (comparison.DeletedFiles.Count > 0)
                {
                    LogTextBox.AppendText("\n删除文件:\n");
                    foreach (var file in comparison.DeletedFiles)
                    {
                        LogTextBox.AppendText($"- {file}\n");
                    }
                }

                if (comparison.ModifiedFiles.Count > 0)
                {
                    LogTextBox.AppendText("\n修改文件:\n");
                    foreach (var file in comparison.ModifiedFiles)
                    {
                        LogTextBox.AppendText($"* {file}\n");
                    }
                }
            }
            catch (Exception ex)
            {
                LogTextBox.AppendText($"错误: {ex.Message}\n");
            }
            finally
            {
                ProgressBar.IsIndeterminate = false;
            }
        }

        private Dictionary<string, string> CalculateFolderChecksums(string folderPath, string algorithm)
        {
            var checksums = new Dictionary<string, string>();
            var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                // 跳过校验文件本身
                if (file.EndsWith("folder_checksum.json"))
                    continue;

                var relativePath = GetRelativePath(file, folderPath);
                var checksum = CalculateFileChecksum(file, algorithm);
                checksums[relativePath] = checksum;

                Dispatcher.Invoke(() => LogTextBox.AppendText($"处理: {relativePath}\n"));
            }

            return checksums;
        }

        private string CalculateFileChecksum(string filePath, string algorithm)
        {
            using (var stream = File.OpenRead(filePath))
            {
                HashAlgorithm hashAlgorithm;
                switch (algorithm)
                {
                    case "SHA1":
                        hashAlgorithm = SHA1.Create();
                        break;
                    case "SHA256":
                        hashAlgorithm = SHA256.Create();
                        break;
                    case "SHA512":
                        hashAlgorithm = SHA512.Create();
                        break;
                    default:
                        hashAlgorithm = MD5.Create();
                        break;
                }

                var hash = hashAlgorithm.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        private string GetRelativePath(string fullPath, string basePath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;

            var uri = new Uri(fullPath);
            var baseUri = new Uri(basePath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(uri).ToString().Replace('/', Path.DirectorySeparatorChar));
        }

        private ComparisonResult CompareChecksums(
            Dictionary<string, string> original,
            Dictionary<string, string> current)
        {
            var result = new ComparisonResult();

            // 查找新增文件
            foreach (var file in current.Keys)
            {
                if (!original.ContainsKey(file))
                {
                    result.AddedFiles.Add(file);
                }
            }

            // 查找删除和修改的文件
            foreach (var file in original.Keys)
            {
                if (!current.ContainsKey(file))
                {
                    result.DeletedFiles.Add(file);
                }
                else if (original[file] != current[file])
                {
                    result.ModifiedFiles.Add(file);
                }
            }

            return result;
        }
    }

    public class ComparisonResult
    {
        public List<string> AddedFiles { get; } = new List<string>();
        public List<string> DeletedFiles { get; } = new List<string>();
        public List<string> ModifiedFiles { get; } = new List<string>();
    }
}