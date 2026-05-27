using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Collections;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.Reflection;
using System.Threading.Tasks;
namespace DracoUI;

public partial class MainWindow : Window
{
    [DllImport("DracoCore.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr SingleValueTimeComplexity(double[] testValues, int testValuesSize, double[] timeValues, int timeValuesSize);
    public ObservableCollection<ExplorerItem> ExplorerData { get; set; }
    private string currentFilePath = string.Empty;
    private List<double> timeValues = new List<double>();
    private static readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);
    bool _isWorking = false;
    public MainWindow()
    {
        InitializeComponent();
        ExplorerData = new ObservableCollection<ExplorerItem>();
        this.DataContext = this;
        using (Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream("DracoUI.cpp.xshd"))
        {
            if (s != null)
            {
                using (XmlTextReader reader = new XmlTextReader(s))
                {
                    CodeEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                }
            }
        }
    }
    private double GetCpuTime(double nValue)
    {
        ProcessStartInfo psi = new ProcessStartInfo("app.exe");
        psi.RedirectStandardInput = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        using (Process p = new Process { StartInfo = psi })
        {
            p.Start();
            p.PriorityClass = ProcessPriorityClass.High;
            p.StandardInput.WriteLine(nValue.ToString());

            if (!p.WaitForExit(3000)) { p.Kill(); return 3000; }

            return p.TotalProcessorTime.TotalMilliseconds;
        }
    }
    private List<double> generateNValues(double probeTime, double probeN)
    {
        double maxN = probeN;
        double minN = maxN / 100;
        List<double> nList = new List<double>();
        double factor = Math.Pow(maxN / minN, 1.0 / 14.0);
        double currentN = minN;
        for (int i = 0; i < 15; i++)
        {
            nList.Add(Math.Floor(currentN));
            currentN *= factor;
        }
        return nList;
    }
    private async Task AnalyzeComplexity()
    {
        await _semaphoreSlim.WaitAsync();
        try
        {
            timeValues.Clear();
            double probeN = 10000;
            double probeTime = 0;
            await Task.Run(() => {
                while (probeTime < 100 && probeN < 100000000)
                {
                    probeN *= 5; 
                    probeTime = GetCpuTime(probeN);
                }
            });
            var dynamicNList = generateNValues(probeTime, probeN);
            await Task.Run(() =>
            {
                foreach (double nValue in dynamicNList)
                {
                    List<double> samples = new List<double>();
                    for (int i = 0; i < 3; i++)
                    {
                        samples.Add(GetCpuTime(nValue));
                    }
                    samples.Sort();
                    double cleanedTime = samples[1];
                    timeValues.Add(Math.Max(cleanedTime, 0.001));
                }
            });
            string rez = "Date colectate:\n";
            for (int i = 0; i < dynamicNList.Count; i++)
                rez += $"N: {dynamicNList[i]:N0} -> T: {timeValues[i]} ms\n";

            MessageBox.Show(rez);

            IntPtr ptr = SingleValueTimeComplexity(dynamicNList.ToArray(), dynamicNList.Count, timeValues.ToArray(), timeValues.Count);
            string? result = Marshal.PtrToStringUTF8(ptr);
            MessageBox.Show($"Verdict Draco: {result}");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Eroare: " + ex.Message);
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
    private async void FangButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isWorking) return;
        _isWorking = true;
        var btn = sender as Button;
        if (btn != null) btn.IsEnabled = false;
        bool isBuildSuccesful = await BuildButton();
        if (isBuildSuccesful)
        {
            await AnalyzeComplexity();
        }
        if(btn != null) btn.IsEnabled = true;
        _isWorking = false;
    }
    private void Output_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            if (e.Delta > 0)
                textBox.LineUp();
            else
                textBox.LineDown();
            e.Handled = true;
        }
    }
    private async Task<bool> BuildButton()
    {
        await _semaphoreSlim.WaitAsync();
        try {
            if (string.IsNullOrEmpty(currentFilePath))
            {
                OutputTabs.SelectedIndex = 0;
                BuildOutput.Text = "[!] Error: No file selected. Please open or save a file first.";
                return false;
            }
            string extension = System.IO.Path.GetExtension(currentFilePath).ToLower();
            if (extension == ".h" || extension == ".hpp")
            {
                OutputTabs.SelectedIndex = 0;
                BuildOutput.Text = "[!] Error: Header files (.h) cannot be compiled directly.\n" +
                                   "Please select the .cpp file that includes this header to build.";
                return false;
            }
            OutputTabs.SelectedIndex = 0;
            BuildOutput.Text = "Forging code... 🔨\n";
            string fileToCompile = string.IsNullOrEmpty(currentFilePath) ? "temp.cpp" : currentFilePath;
            File.WriteAllText(fileToCompile, CodeEditor.Text);

            ProcessStartInfo si = new ProcessStartInfo
            {
                FileName = "g++",
                Arguments = $"\"{fileToCompile}\" -o app.exe -pthread",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process proc = new Process { StartInfo = si })
            {
                proc.Start();
                string errors = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();

                if (proc.ExitCode == 0)
                {
                    BuildOutput.Text += "---------------------------------\n";
                    BuildOutput.Foreground = Brushes.LightGreen;
                    BuildOutput.Text += "SUCCESS: Build finished. ";
                    return true;
                }
                else
                {
                    BuildOutput.Text += "---------------------------------\n";
                    BuildOutput.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FA003F"));
                    BuildOutput.Text += "FAILED:\n" + errors;
                    return false; 
                }
            }
        }
        catch (Exception ex)
        {
            BuildOutput.Text += "SYSTEM ERROR: " + ex.Message;
            return false;
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
    private async void BuildButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isWorking) return;
        _isWorking = true;
        await BuildButton();
        _isWorking = false;
    }
    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }
    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.WindowState == WindowState.Maximized)
            this.WindowState = WindowState.Normal;
        else
            this.WindowState = WindowState.Maximized;
    }
    private void MenuButtonHover(object sender, RoutedEventArgs e)
    {
        Button bt = (Button)sender;
        bt.Background = new SolidColorBrush(Color.FromRgb(34, 34, 34));
    }
    private void MenuButtonLeave(object sender, RoutedEventArgs e)
    {
        Button bt = (Button)sender;
        bt.Background = new SolidColorBrush(Color.FromRgb(13, 13, 13));
    }
    private void PopupButtonHover(object sender, RoutedEventArgs e)
    {
        Button bt = (Button)sender;
        bt.Background = new SolidColorBrush(Color.FromRgb(51, 51, 51));
    }
    private void PopupButtonLeave(object sender, RoutedEventArgs e)
    {
        Button bt = (Button)sender;
        bt.Background = new SolidColorBrush(Color.FromRgb(26, 26, 26));
    }
    private void UtilityButtonHover(object sender, RoutedEventArgs e)
    {
        Button bt = (Button)sender;
        bt.Background = new SolidColorBrush(Color.FromRgb(34, 34, 34));
    }
    private void UtilityButtonLeave(object sender, RoutedEventArgs e)
    {
        Button bt = (Button)sender;
        bt.Background = new SolidColorBrush(Color.FromRgb(0, 0, 0));
    }
    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
    private void FileButtonClick(object sender, RoutedEventArgs e)
    {
        FileOptions.IsOpen = true;
    }
    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if(_isWorking && (e.Key == Key.F5 || e.Key == Key.F8 || e.Key == Key.F9))
        {
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.F5:
                e.Handled = true;
                _isWorking = true;
                await BuildButton();
                _isWorking = false;
                break;
            case Key.F8:
                e.Handled = true;
                _isWorking = true;
                await RunProgram();
                _isWorking = false;
                break;
            case Key.F9:
                e.Handled = true;
                _isWorking = true;
                if (await BuildButton())
                {
                    await RunProgram();
                }
                _isWorking = false;
                break;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            // CTRL + O (Open File)
            if (e.Key == Key.O)
            {
                e.Handled = true;
                HomeOpenFile();
            }

            // CTRL + S (Save)
            else if (e.Key == Key.S)
            {
                e.Handled = true;
                SaveFile();
            }

            // CTRL + N (New File)
            else if (e.Key == Key.N)
            {
                e.Handled = true;
                NewFile();
            }
            else if(e.Key == Key.W)
            {
                e.Handled = true;
                CloseEditor();
            }
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            // CTRL + SHIFT + O
            if (e.Key == Key.O)
            {
                e.Handled = true;
                HomeOpenFolder();
            }
            else if(e.Key == Key.S)
            {
                e.Handled = true;
                SaveFileAs();
            }
        }
    }
    private async Task RunProgram()
    {
        await _semaphoreSlim.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(currentFilePath))
            {
                File.WriteAllText(currentFilePath, CodeEditor.Text);
            }
            string extension = System.IO.Path.GetExtension(currentFilePath).ToLower();
            if (extension == ".h" || extension == ".hpp")
            {
                OutputTabs.SelectedIndex = 1;
                TerminalOutput.Text = "[!] Error: Cannot run a header file directly.";
                return;
            }
            string? projectDirectory = System.IO.Path.GetDirectoryName(currentFilePath);
            string fullExePath = System.IO.Path.Combine(projectDirectory ?? "", "app.exe");
            if (!File.Exists("app.exe"))
            {
                OutputTabs.SelectedIndex = 0;
                BuildOutput.Text += "\n[Error] No executable found. Build first!";
                return;
            }

            OutputTabs.SelectedIndex = 1;
            TerminalOutput.Clear();
            TerminalOutput.Text = "--- Running ---\n";

            using (Process runProcess = new Process())
            {
                runProcess.StartInfo.FileName = "app.exe";
                runProcess.StartInfo.WorkingDirectory = projectDirectory;
                runProcess.StartInfo.RedirectStandardOutput = true;
                runProcess.StartInfo.UseShellExecute = false;
                runProcess.StartInfo.CreateNoWindow = false;
                runProcess.Start();

                var readTask = runProcess.StandardOutput.ReadToEndAsync();
                var waitTask = runProcess.WaitForExitAsync();
                if(await Task.WhenAny(waitTask, Task.Delay(30000)) != waitTask)
                {
                    runProcess.Kill();
                    await readTask;
                    TerminalOutput.Text += "\n[!] Process killed (Timeout 30s). No Input received";
                }
                else
                {
                    string result = await readTask;
                    TerminalOutput.Text += result;
                    TerminalOutput.Text += "\n\n--- Process finished with code " + runProcess.ExitCode + " ---";

                }
            }
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isWorking) return;
        _isWorking = true;
        await RunProgram();
        _isWorking = false;
    }
    private bool AddFileToTree(ObservableCollection<ExplorerItem> list, string targetDirPath, ExplorerItem newFile)
    {
        foreach (ExplorerItem item in list)
        {
            if (item.IsDirectory)
            {
                if (item.FullPath == targetDirPath)
                {
                    bool dejaExista = false;
                    foreach (ExplorerItem child in item.Items)
                    {
                        if (child.FullPath == newFile.FullPath)
                        {
                            dejaExista = true;
                            break;
                        }
                    }
                    if (!dejaExista)
                    {
                        item.Items.Add(newFile);
                    }
                    return true;
                }
                if (AddFileToTree(item.Items, targetDirPath, newFile))
                {
                    return true;
                }
            }
        }
        return false;
    }
    private void CloseEditor()
    {
        CodeEditor.Text = "";
        ExplorerData.Clear();
        currentFilePath = "";
        HomeView.Visibility = Visibility.Visible;
        EditorView.Visibility = Visibility.Collapsed;   
    }
    private void CloseEditor_Click(object sender, RoutedEventArgs e)
    {
        FileOptions.IsOpen = false;
        CloseEditor();
    }
    private void ExitApp()
    {
        Application.Current.Shutdown();
    }
    private void ExitApp_Click(object sender, RoutedEventArgs e)
    {
        FileOptions.IsOpen = false;
        ExitApp();
    }
    private void SaveFileAs()
    {
        SaveFileDialog sfd = new SaveFileDialog();
        sfd.Filter = "C++ Files (*.cpp; *.h)|*.cpp;*.h";
        sfd.Title = "Save Soul As...";
        if (!string.IsNullOrEmpty(currentFilePath))
        {
            sfd.InitialDirectory = System.IO.Path.GetDirectoryName(currentFilePath);
        }
        else if (ExplorerData.Count > 0 && ExplorerData[0].IsDirectory)
        {
            sfd.InitialDirectory = ExplorerData[0].FullPath;
        }
        if (sfd.ShowDialog() == true)
        {
            try
            {
                string FileName = sfd.FileName;
                string shortName = System.IO.Path.GetFileName(FileName);
                string newFileDir = System.IO.Path.GetDirectoryName(FileName) ?? string.Empty;
                File.WriteAllText(FileName, CodeEditor.Text);
                currentFilePath = FileName;
                if (ExplorerData.Count > 0 && ExplorerData[0].IsDirectory && newFileDir.StartsWith(ExplorerData[0].FullPath))
                {
                    ExplorerItem newItem = new ExplorerItem(shortName, FileName, false);
                    bool gasit = AddFileToTree(ExplorerData, newFileDir, newItem);

                    if (!gasit)
                    {
                        string rootPath = ExplorerData[0].FullPath;
                        string rootName = ExplorerData[0].Title;
                        ExplorerData.Clear();
                        ExplorerItem refreshedRoot = new ExplorerItem(rootName, rootPath, true);
                        ExplorerData.Add(refreshedRoot);
                        LoadDirectory(rootPath, refreshedRoot);
                    }
                }
                else
                {
                    ExplorerData.Clear();
                    ExplorerData.Add(new ExplorerItem(shortName, FileName, false));
                }
                HomeView.Visibility = Visibility.Collapsed;
                EditorView.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Draco couldn't reforge this soul: " + ex.Message);
            }
        }
    }
    private void SaveFileAs_Click(Object sender, RoutedEventArgs e)
    {
        FileOptions.IsOpen = false;
        SaveFileAs();
    }
    private void SaveFile()
    {
        if (string.IsNullOrEmpty(currentFilePath)) return;
        try
        {
            File.WriteAllText(currentFilePath, CodeEditor.Text);
        }
        catch(Exception ex) {

            MessageBox.Show("Draco failed to bind the soul to the file: " + ex.Message);
        }

    }
    private void SaveFile_Click(Object sender, RoutedEventArgs e)
    {
        FileOptions.IsOpen = false;
        SaveFile();
    }
    private void NewFile()
    {
        SaveFileDialog sfd = new SaveFileDialog();
        sfd.Filter = "C++ Files (*.cpp; *.h)|*.cpp;*.h";
        sfd.Title = "Forge New File";
        if (ExplorerData.Count > 0 && ExplorerData[0].IsDirectory)
        {
            sfd.InitialDirectory = ExplorerData[0].FullPath;
        }

        if (sfd.ShowDialog() == true)
        {
            try
            {
                string FileName = sfd.FileName;
                string shortName = System.IO.Path.GetFileName(FileName);
                string newFileDir = System.IO.Path.GetDirectoryName(FileName) ?? string.Empty;
                File.WriteAllText(FileName, "");
                CodeEditor.Text = "";
                currentFilePath = FileName;
                if (ExplorerData.Count > 0 && ExplorerData[0].IsDirectory && newFileDir.StartsWith(ExplorerData[0].FullPath)) { 
                    ExplorerItem newItem = new ExplorerItem(shortName, FileName, false);
                    bool gasit = AddFileToTree(ExplorerData, newFileDir, newItem);
                    if (!gasit)
                    {
                        string rootPath = ExplorerData[0].FullPath;
                        string rootName = ExplorerData[0].Title;
                        ExplorerData.Clear();
                        ExplorerItem refreshedRoot = new ExplorerItem(rootName, rootPath, true);
                        ExplorerData.Add(refreshedRoot);
                        LoadDirectory(rootPath, refreshedRoot);
                    }
                }
                else
                {
                    ExplorerData.Clear();
                    ExplorerData.Add(new ExplorerItem(shortName, FileName, false));
                }
                HomeView.Visibility = Visibility.Collapsed;
                EditorView.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Draco couldn't forge this file: " + ex.Message);
            }
        }
    }
    private void NewFile_Click(object sender, RoutedEventArgs e)
    {
        FileOptions.IsOpen = false;
        NewFile();
    }
    private void HomeOpenFile()
    {
        OpenFileDialog ofd = new OpenFileDialog();
        ofd.Filter = "C++ Files (*.cpp; *.h)|*.cpp;*.h";
        ofd.Multiselect = false;
        ofd.Title = "Select File";
        if (ofd.ShowDialog() == true)
        {
            try
            {
                string FileName = ofd.FileName;
                currentFilePath = FileName;
                string shortName = System.IO.Path.GetFileName(FileName);
                CodeEditor.Text = File.ReadAllText(FileName);
                ExplorerData.Clear();
                ExplorerData.Add(new ExplorerItem(shortName, FileName, false));
                HomeView.Visibility = Visibility.Collapsed;
                EditorView.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
    }
    private void HomeOpenFile_Click(object sender, RoutedEventArgs e)
    {
        FileOptions.IsOpen = false;
        HomeOpenFile();
    }
    private void LoadDirectory(string path, ExplorerItem parent)
    {
        try
        {
            foreach (string directory in Directory.GetDirectories(path))
            {
                string directoryName = System.IO.Path.GetFileName(directory);
                ExplorerItem subfolder = new ExplorerItem(directoryName, directory, true);
                parent.Items.Add(subfolder);
                LoadDirectory(directory, subfolder);
            }
            foreach (string file in Directory.GetFiles(path))
            {
                string extension = System.IO.Path.GetExtension(file).ToLower();
                if (extension == ".cpp" || extension == ".h")
                {
                    string fileName = System.IO.Path.GetFileName(file);
                    parent.Items.Add(new ExplorerItem(fileName, file, false));
                }
            }
        }
        catch(Exception)
        {
            return;
        }
    }
    private void HomeOpenFolder()
    {
        OpenFolderDialog ofd = new OpenFolderDialog();
        ofd.Multiselect = false;
        ofd.Title = "Select Project";
        if (ofd.ShowDialog() == true)
        {
            try
            {
                string folderPath = ofd.FolderName;
                currentFilePath = "";
                string folderName = System.IO.Path.GetFileName(folderPath);
                CodeEditor.Text = "";
                ExplorerData.Clear();
                ExplorerItem newFolder = new ExplorerItem(folderName, ofd.FolderName, true);
                LoadDirectory(folderPath, newFolder);
                ExplorerData.Add(newFolder);
                HomeView.Visibility = Visibility.Collapsed;
                EditorView.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
    }
    private void HomeOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        FileOptions.IsOpen = false;   
        HomeOpenFolder();
    }
    private void ChangeCodeDisplayed(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        ExplorerItem? selectedItem = e.NewValue as ExplorerItem;
        if (selectedItem != null && !selectedItem.IsDirectory)
        {
            CodeEditor.Clear();
            try
            {
                currentFilePath = selectedItem.FullPath;
                CodeEditor.Text = File.ReadAllText(selectedItem.FullPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Draco couldn't read this soul: " + ex.Message);
            }
        }
    }
}
public class ExplorerItem
{
    public string Title { get; set; }
    public string FullPath { get; set; }
    public bool IsDirectory { get; set; }
    public ObservableCollection<ExplorerItem> Items { get; set; }
    public ExplorerItem(string Title, string FullPath, bool IsDirectory)
    {
        this.Title = Title;
        this.FullPath = FullPath;
        this.IsDirectory = IsDirectory;
        Items = new ObservableCollection<ExplorerItem>();
    }
}