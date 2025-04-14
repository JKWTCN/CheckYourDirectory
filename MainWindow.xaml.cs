using Microsoft.Win32;
using Newtonsoft.Json;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;

namespace CheckYourDirectory
{
    public class ChecksumData
    {
        /// <summary>
        /// 使用的哈希算法(MD5/SHA1/SHA256/SHA512)
        /// </summary>
        public string HashAlgorithm { get; set; }

        /// <summary>
        /// 文件创建时间(用于记录快照时间)
        /// </summary>
        public DateTime CreationTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 文件校验值字典(相对路径->校验值)
        /// </summary>
        public Dictionary<string, string> FileChecksums { get; set; } = new Dictionary<string, string>();
    }
    public partial class MainWindow : Window
    {
        private ChecksumData _loadedChecksumData;

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

            var folderPath = FolderPathTextBox.Text;

            // 弹出保存文件对话框
            var saveDialog = new SaveFileDialog
            {
                Title = "保存校验文件",
                Filter = "JSON文件|*.json",
                FileName = "folder_checksum.json",
                DefaultExt = ".json"
            };

            if (saveDialog.ShowDialog() != true)
            {
                return; // 用户取消了保存
            }

            try
            {
                ProgressBar.IsIndeterminate = true;
                LogTextBox.Text = "正在计算文件校验值...\n";

                var algorithm = ((ComboBoxItem)HashAlgorithmComboBox.SelectedItem).Content.ToString();
                var fileChecksums = await Task.Run(() => CalculateFolderChecksums(folderPath, algorithm));

                var checksumData = new ChecksumData
                {
                    HashAlgorithm = algorithm,
                    FileChecksums = fileChecksums
                };

                var json = JsonConvert.SerializeObject(checksumData, Formatting.Indented);
                File.WriteAllText(saveDialog.FileName, json);

                LogTextBox.AppendText($"校验文件已保存: {saveDialog.FileName}\n");
                LogTextBox.AppendText($"使用算法: {algorithm}\n");
                LogTextBox.AppendText($"共处理 {fileChecksums.Count} 个文件\n");
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


        private void OpenChecksumFile_Click(object sender, RoutedEventArgs e)
        {
            var openDialog = new OpenFileDialog
            {
                Title = "打开校验文件",
                Filter = "JSON文件|*.json",
                DefaultExt = ".json"
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(openDialog.FileName);
                    var checksumData = JsonConvert.DeserializeObject<ChecksumData>(json);
                    _loadedChecksumData = checksumData;

                    LogTextBox.Text = $"已加载校验文件: {openDialog.FileName}\n";
                    LogTextBox.AppendText($"使用算法: {checksumData.HashAlgorithm}\n");
                    LogTextBox.AppendText($"共包含 {checksumData.FileChecksums.Count} 个文件的校验信息\n");

                    // 更新UI显示当前使用的算法
                    foreach (ComboBoxItem item in HashAlgorithmComboBox.Items)
                    {
                        if (item.Content.ToString() == checksumData.HashAlgorithm)
                        {
                            item.IsSelected = true;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"加载校验文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void CompareFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_loadedChecksumData == null)
            {
                MessageBox.Show("请先打开校验文件", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(FolderPathTextBox.Text))
            {
                MessageBox.Show("请先选择文件夹", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var folderPath = FolderPathTextBox.Text;
            var algorithm = _loadedChecksumData.HashAlgorithm; // 使用校验文件中指定的算法

            try
            {
                ProgressBar.IsIndeterminate = true;
                LogTextBox.AppendText($"\n正在使用 {algorithm} 算法比对文件夹内容...\n");

                var currentData = await Task.Run(() => CalculateFolderChecksums(folderPath, algorithm));

                // 比较结果
                var comparison = CompareChecksums(_loadedChecksumData.FileChecksums, currentData);
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
                HashAlgorithm hashAlgorithm = algorithm switch
                {
                    "SHA1" => SHA1.Create(),
                    "SHA256" => SHA256.Create(),
                    "SHA512" => SHA512.Create(),
                    _ => MD5.Create(),
                };

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