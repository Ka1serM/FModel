﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutoUpdaterDotNET;
using csharp_wick;
using FModel.Converter;
using FModel.Forms;
using FModel.Parser.Banners;
using FModel.Parser.Challenges;
using FModel.Methods.BackupPAKs.Parser.AESKeyParser;
using FModel.Parser.Items;
using FModel.Parser.Quests;
using FModel.Properties;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ScintillaNET;
using ScintillaNET_FindReplaceDialog;
using Image = System.Drawing.Image;
using Settings = FModel.Properties.Settings;

namespace FModel
{
    public partial class MainWindow : Form
    {
        #region EVERYTHING WE NEED
        FindReplace _myFindReplace;
        public Stopwatch StopWatch;
        private static string[] _paksArray;
        public static string[] PakAsTxt;
        private static Dictionary<string, long> _questStageDict;
        private static Dictionary<string, string> _diffToExtract;
        private static string _backupFileName;
        private static string[] _backupDynamicKeys;
        private static List<string> _itemsToDisplay;
        public static string ExtractedFilePath;
        public static string[] SelectedItemsArray;
        public static string[] SelectedChallengesArray;
        #endregion

        #region DLLIMPORT
        [DllImport("wininet.dll")]
        private extern static bool InternetGetConnectedState(out int description, int reservedValue);
        public static bool IsInternetAvailable()
        {
            return InternetGetConnectedState(description: out _, reservedValue: 0);
        }
        [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string pszSubIdList);
        public static void SetTreeViewTheme(IntPtr treeHandle)
        {
            SetWindowTheme(treeHandle, "explorer", null);
        }
        #endregion

        public MainWindow()
        {
            InitializeComponent();

            toolStripStatusLabel1.Text += @" " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString().Substring(0, 5);

            treeView1.Sort();
            //REMOVE SPACE CAUSED BY SIZING GRIP
            statusStrip1.Padding = new Padding(statusStrip1.Padding.Left, statusStrip1.Padding.Top, statusStrip1.Padding.Left, statusStrip1.Padding.Bottom);

            // Create instance of FindReplace with reference to a ScintillaNET control.
            _myFindReplace = new FindReplace(scintilla1); // For WinForms
            _myFindReplace.Window.StartPosition = FormStartPosition.CenterScreen;
            // Tie in FindReplace event
            _myFindReplace.KeyPressed += MyFindReplace_KeyPressed;
            // Tie in Scintilla event
            scintilla1.KeyDown += scintilla1_KeyDown;
        }

        #region USEFUL METHODS
        private void UpdateConsole(string textToDisplay, Color seColor, string seText)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, Color, string>(UpdateConsole), textToDisplay, seColor, seText);
                return;
            }

            toolStripStatusLabel2.Text = textToDisplay;
            toolStripStatusLabel3.BackColor = seColor;
            toolStripStatusLabel3.Text = seText;
        }
        private void AppendText(string text, Color color, bool addNewLine = false, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, Color, bool, HorizontalAlignment>(AppendText), text, color, addNewLine, align);
                return;
            }
            richTextBox1.SuspendLayout();
            richTextBox1.SelectionColor = color;
            richTextBox1.SelectionAlignment = align;
            richTextBox1.AppendText(addNewLine
                ? $"{text}{Environment.NewLine}"
                : text);
            richTextBox1.ScrollToCaret();
            richTextBox1.ResumeLayout();
        }
        #endregion

        #region LOAD & LEAVE
        //METHODS
        private void AddPaKs(IEnumerable<string> thePaks, int index)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<IEnumerable<string>, int>(AddPaKs), thePaks, index);
                return;
            }
            loadOneToolStripMenuItem.DropDownItems.Add(Path.GetFileName(thePaks.ElementAt(index)));
        }
        private void FillWithPaKs()
        {
            if (!Directory.Exists(Settings.Default.PAKsPath))
            {
                loadOneToolStripMenuItem.Enabled = false;
                loadAllToolStripMenuItem.Enabled = false;
                backupPAKsToolStripMenuItem.Enabled = false;

                UpdateConsole(".PAK Files Path is missing", Color.FromArgb(255, 244, 66, 66), "Error");
            }
            else
            {
                IEnumerable<string> yourPaKs = Directory.GetFiles(Settings.Default.PAKsPath).Where(x => x.EndsWith(".pak"));
                int count = 0;
                var thePaks = yourPaKs as string[] ?? yourPaKs.ToArray();
                foreach (var dummy in thePaks) count++;
                _paksArray = new string[count];
                for (int i = 0; i < thePaks.Count(); i++)
                {
                    AddPaKs(thePaks, i);
                    _paksArray[i] = Path.GetFileName(thePaks.ElementAt(i));
                }
            }
        }
        private void KeyCheck()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(KeyCheck));
                return;
            }
            AESKeyTextBox.Text = @"0x" + Settings.Default.AESKey;
        }
        private void SetScintillaStyle()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(SetScintillaStyle));
                return;
            }

            scintilla1.Styles[Style.Json.Default].ForeColor = Color.Silver;
            scintilla1.Styles[Style.Json.BlockComment].ForeColor = Color.FromArgb(0, 128, 0);
            scintilla1.Styles[Style.Json.LineComment].ForeColor = Color.FromArgb(0, 128, 0);
            scintilla1.Styles[Style.Json.Number].ForeColor = Color.Green;
            scintilla1.Styles[Style.Json.PropertyName].ForeColor = Color.SteelBlue;
            scintilla1.Styles[Style.Json.String].ForeColor = Color.OrangeRed;
            scintilla1.Styles[Style.Json.StringEol].BackColor = Color.OrangeRed;
            scintilla1.Styles[Style.Json.Operator].ForeColor = Color.Black;
            scintilla1.Styles[Style.LineNumber].ForeColor = Color.DarkGray;
            var nums = scintilla1.Margins[1];
            nums.Width = 30;
            nums.Type = MarginType.Number;
            nums.Sensitive = true;
            nums.Mask = 0;

            scintilla1.ClearCmdKey(Keys.Control | Keys.F);
            scintilla1.ClearCmdKey(Keys.Control | Keys.Z);
            scintilla1.Lexer = Lexer.Json;
        }

        //EVENTS
        private async void MainWindow_Load(object sender, EventArgs e)
        {
            AutoUpdater.Start("https://dl.dropbox.com/s/3kv2pukqu6tj1r0/FModel.xml?dl=0");

            SetTreeViewTheme(treeView1.Handle);
            _backupFileName = "\\FortniteGame_" + DateTime.Now.ToString("MMddyyyy") + ".txt";

            // Copy user settings from previous application version if necessary
            if (Settings.Default.UpdateSettings)
            {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                Settings.Default.Save();
            }

            await Task.Run(() => {
                FillWithPaKs();
                KeyCheck();
                Utilities.SetOutputFolder();
                Utilities.SetFolderPermission(App.DefaultOutputPath);
                Utilities.JohnWickCheck();
                Utilities.CreateDefaultFolders();
                SetScintillaStyle();
                FontUtilities.SetFont();
            });
        }
        private void MainWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            Environment.Exit(Environment.ExitCode);
        }
        private void differenceModeToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            if (differenceModeToolStripMenuItem.Checked)
            {
                loadAllToolStripMenuItem.Text = @"Load Difference";
                loadOneToolStripMenuItem.Enabled = false;
                updateModeToolStripMenuItem.Enabled = true;
            }
            if (differenceModeToolStripMenuItem.Checked == false)
            {
                loadAllToolStripMenuItem.Text = @"Load All PAKs";
                loadOneToolStripMenuItem.Enabled = true;
                updateModeToolStripMenuItem.Enabled = false;
                if (updateModeToolStripMenuItem.Checked)
                    updateModeToolStripMenuItem.Checked = false;
            }
            if (updateModeToolStripMenuItem.Checked == false && differenceModeToolStripMenuItem.Checked == false)
            {
                loadAllToolStripMenuItem.Text = @"Load All PAKs";
            }
        }
        private void updateModeToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            if (updateModeToolStripMenuItem.Checked)
            {
                loadAllToolStripMenuItem.Text = @"Load And Extract Difference";
                var updateModeForm = new UpdateModeSettings();
                if (Application.OpenForms[updateModeForm.Name] == null)
                {
                    updateModeForm.Show();
                }
                else
                {
                    Application.OpenForms[updateModeForm.Name].Focus();
                }
            }
            if (updateModeToolStripMenuItem.Checked == false)
            {
                loadAllToolStripMenuItem.Text = @"Load Difference";
            }
        }
        private void scintilla1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.F)
            {
                _myFindReplace.ShowFind();
                e.SuppressKeyPress = true;
            }
            else if (e.Shift && e.KeyCode == Keys.F3)
            {
                _myFindReplace.Window.FindPrevious();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.F3)
            {
                _myFindReplace.Window.FindNext();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.H)
            {
                _myFindReplace.ShowReplace();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.I)
            {
                _myFindReplace.ShowIncrementalSearch();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.G)
            {
                GoTo myGoTo = new GoTo((Scintilla)sender);
                myGoTo.ShowGoToDialog();
                e.SuppressKeyPress = true;
            }
        }
        private void MyFindReplace_KeyPressed(object sender, KeyEventArgs e)
        {
            scintilla1_KeyDown(sender, e);
        }

        //FORMS
        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var settingsForm = new Forms.Settings();
            if (Application.OpenForms[settingsForm.Name] == null)
            {
                settingsForm.Show();
            }
            else
            {
                Application.OpenForms[settingsForm.Name].Focus();
            }
        }
        private void aboutFModelToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var aboutForm = new About();
            if (Application.OpenForms[aboutForm.Name] == null)
            {
                aboutForm.Show();
            }
            else
            {
                Application.OpenForms[aboutForm.Name].Focus();
            }
        }
        private void searchToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var searchForm = new SearchFiles();
            if (Application.OpenForms[searchForm.Name] == null)
            {
                searchForm.Show();
            }
            else
            {
                Application.OpenForms[searchForm.Name].Focus();
            }
            searchForm.FormClosing += (o, c) =>
            {
                OpenMe();
            };
        }
        #endregion

        #region PAKLIST & FILL TREE
        //METHODS
        private void RegisterPaKsinDict(string[] allYourPaKs, ToolStripItemClickedEventArgs theSinglePak = null, bool loadAllPaKs = false)
        {
            for (int i = 0; i < allYourPaKs.Length; i++)
            {
                string arCurrentUsedPak = allYourPaKs[i]; //SET CURRENT PAK
                string arCurrentUsedPakGuid = ThePak.ReadPakGuid(Settings.Default.PAKsPath + "\\" + arCurrentUsedPak); //SET CURRENT PAK GUID

                if (arCurrentUsedPakGuid == "0-0-0-0") //NO DYNAMIC PAK IN DICTIONARY
                {
                    try
                    {
                        JohnWick.MyExtractor = new PakExtractor(Settings.Default.PAKsPath + "\\" + arCurrentUsedPak, Settings.Default.AESKey);
                    }
                    catch (Exception)
                    {
                        break;
                    }

                    if (JohnWick.MyExtractor.GetFileList() != null)
                    {
                        ThePak.PaksMountPoint.Add(arCurrentUsedPak, JohnWick.MyExtractor.GetMountPoint().Substring(9));

                        if (loadAllPaKs)
                            if (!File.Exists(App.DefaultOutputPath + "\\FortnitePAKs.txt"))
                                File.Create(App.DefaultOutputPath + "\\FortnitePAKs.txt").Dispose();

                        string[] CurrentUsedPakLines = JohnWick.MyExtractor.GetFileList().ToArray();
                        for (int ii = 0; ii < CurrentUsedPakLines.Length; ii++)
                        {
                            CurrentUsedPakLines[ii] = JohnWick.MyExtractor.GetMountPoint().Substring(6) + CurrentUsedPakLines[ii];

                            string CurrentUsedPakFileName = CurrentUsedPakLines[ii].Substring(CurrentUsedPakLines[ii].LastIndexOf("/", StringComparison.Ordinal) + 1);
                            if (CurrentUsedPakFileName.Contains(".uasset") || CurrentUsedPakFileName.Contains(".uexp") || CurrentUsedPakFileName.Contains(".ubulk"))
                            {
                                if (!ThePak.AllpaksDictionary.ContainsKey(CurrentUsedPakFileName.Substring(0, CurrentUsedPakFileName.LastIndexOf(".", StringComparison.Ordinal))))
                                {
                                    ThePak.AllpaksDictionary.Add(CurrentUsedPakFileName.Substring(0, CurrentUsedPakFileName.LastIndexOf(".", StringComparison.Ordinal)), arCurrentUsedPak);
                                }
                            }
                            else
                            {
                                if (!ThePak.AllpaksDictionary.ContainsKey(CurrentUsedPakFileName))
                                {
                                    ThePak.AllpaksDictionary.Add(CurrentUsedPakFileName, arCurrentUsedPak);
                                }
                            }
                        }
                        if (loadAllPaKs)
                        {
                            UpdateConsole(".PAK mount point: " + JohnWick.MyExtractor.GetMountPoint().Substring(9), Color.FromArgb(255, 244, 132, 66), "Waiting");

                            File.AppendAllLines(App.DefaultOutputPath + "\\FortnitePAKs.txt", CurrentUsedPakLines);

                            ThePak.CurrentUsedPak = null;
                            ThePak.CurrentUsedPakGuid = null;
                        }
                    }
                }
                if (theSinglePak != null)
                {
                    ThePak.CurrentUsedPak = theSinglePak.ClickedItem.Text;
                    ThePak.CurrentUsedPakGuid = ThePak.ReadPakGuid(Settings.Default.PAKsPath + "\\" + ThePak.CurrentUsedPak);

                    if (arCurrentUsedPak == theSinglePak.ClickedItem.Text && JohnWick.MyExtractor.GetFileList() != null)
                        PakAsTxt = JohnWick.MyExtractor.GetFileList().ToArray();
                }
            }
            if (theSinglePak != null && ThePak.ReadPakGuid(Settings.Default.PAKsPath + "\\" + theSinglePak.ClickedItem.Text) != "0-0-0-0") //LOADING DYNAMIC PAK
            {
                ThePak.CurrentUsedPak = theSinglePak.ClickedItem.Text;
                ThePak.CurrentUsedPakGuid = ThePak.ReadPakGuid(Settings.Default.PAKsPath + "\\" + ThePak.CurrentUsedPak);

                try
                {
                    JohnWick.MyExtractor = new PakExtractor(Settings.Default.PAKsPath + "\\" + theSinglePak.ClickedItem.Text, Settings.Default.AESKey);

                    if (JohnWick.MyExtractor.GetFileList() != null)
                    {
                        ThePak.PaksMountPoint.Add(theSinglePak.ClickedItem.Text, JohnWick.MyExtractor.GetMountPoint().Substring(9));
                        PakAsTxt = JohnWick.MyExtractor.GetFileList().ToArray();
                    }
                }
                catch (Exception)
                {
                    UpdateConsole("Can't read " + theSinglePak.ClickedItem.Text + " with this key", Color.FromArgb(255, 244, 66, 66), "Error");
                }
            }
            UpdateConsole("Building tree, please wait...", Color.FromArgb(255, 244, 132, 66), "Loading");
        }
        private void TreeParsePath(TreeNodeCollection nodeList, string path) //https://social.msdn.microsoft.com/Forums/en-US/c75c1804-6933-40ba-b17a-0e36ae8bcbb5/how-to-create-a-tree-view-with-full-paths?forum=csharplanguage
        {
            TreeNode node;
            string folder;
            int p = path.IndexOf('/');

            if (p == -1)
            {
                folder = path;
                path = "";
            }
            else
            {
                folder = path.Substring(0, p);
                path = path.Substring(p + 1, path.Length - (p + 1));
            }

            node = null;
            foreach (TreeNode item in nodeList)
            {
                if (item.Text == folder)
                {
                    node = item;
                }
            }
            if (node == null)
            {
                node = new TreeNode(folder);
                Invoke(new Action(() =>
                {
                    nodeList.Add(node);
                }));
            }
            if (path != "")
            {
                TreeParsePath(node.Nodes, path);
            }
        }
        private void ComparePaKs()
        {
            PakAsTxt = File.ReadAllLines(App.DefaultOutputPath + "\\FortnitePAKs.txt");
            File.Delete(App.DefaultOutputPath + "\\FortnitePAKs.txt");

            //ASK DIFFERENCE FILE AND COMPARE
            OpenFileDialog theDialog = new OpenFileDialog();
            theDialog.Title = @"Choose your Backup PAK File";
            theDialog.InitialDirectory = App.DefaultOutputPath + "\\Backup";
            theDialog.Multiselect = false;
            theDialog.Filter = @"TXT Files (*.txt)|*.txt|All Files (*.*)|*.*";
            Invoke(new Action(() =>
            {
                if (theDialog.ShowDialog() == DialogResult.OK)
                {
                    String[] linesA = File.ReadAllLines(theDialog.FileName);
                    for (int i = 0; i < linesA.Length; i++)
                        if (!linesA[i].StartsWith("../"))
                            linesA[i] = "../" + linesA[i];

                    IEnumerable<String> onlyB = PakAsTxt.Except(linesA);
                    IEnumerable<String> removed = linesA.Except(PakAsTxt);

                    File.WriteAllLines(App.DefaultOutputPath + "\\Result.txt", onlyB);
                    File.WriteAllLines(App.DefaultOutputPath + "\\Removed.txt", removed);
                }
            }));

            //GET REMOVED FILES
            var removedTxt = File.ReadAllLines(App.DefaultOutputPath + "\\Removed.txt");
            File.Delete(App.DefaultOutputPath + "\\Removed.txt");

            List<string> removedItems = new List<string>();
            for (int i = 0; i < removedTxt.Length; i++)
            {
                if (removedTxt[i].Contains("FortniteGame/Content/Athena/Items/Cosmetics/"))
                    removedItems.Add(removedTxt[i].Substring(0, removedTxt[i].LastIndexOf(".", StringComparison.Ordinal)));
            }
            if (removedItems.Count != 0)
            {
                Invoke(new Action(() =>
                {
                    AppendText("Items Removed/Renamed:", Color.Red, true);
                    removedItems = removedItems.Distinct().ToList();
                    for (int ii = 0; ii < removedItems.Count; ii++)
                        AppendText("    - " + removedItems[ii], Color.Black, true);
                }));
            }

            PakAsTxt = File.ReadAllLines(App.DefaultOutputPath + "\\Result.txt");
            File.Delete(App.DefaultOutputPath + "\\Result.txt");
        }
        private void CreatePakList(ToolStripItemClickedEventArgs selectedPak = null, bool loadAllPaKs = false, bool getDiff = false, bool updateMode = false)
        {
            ThePak.AllpaksDictionary = new Dictionary<string, string>();
            _diffToExtract = new Dictionary<string, string>();
            ThePak.PaksMountPoint = new Dictionary<string, string>();
            Settings.Default.AESKey = AESKeyTextBox.Text.Substring(2).ToUpper();
            Settings.Default.Save();

            if (selectedPak != null)
            {
                UpdateConsole(Settings.Default.PAKsPath + "\\" + selectedPak.ClickedItem.Text, Color.FromArgb(255, 244, 132, 66), "Loading");

                //ADD TO DICTIONNARY
                RegisterPaKsinDict(_paksArray, selectedPak);

                if (PakAsTxt != null)
                {
                    Invoke(new Action(() =>
                    {
                        treeView1.BeginUpdate();
                        for (int i = 0; i < PakAsTxt.Length; i++)
                        {
                            TreeParsePath(treeView1.Nodes, PakAsTxt[i].Replace(PakAsTxt[i].Split('/').Last(), ""));
                        }
                        treeView1.EndUpdate();
                    }));
                    UpdateConsole(Settings.Default.PAKsPath + "\\" + selectedPak.ClickedItem.Text, Color.FromArgb(255, 66, 244, 66), "Success");
                }
                else
                    UpdateConsole("Can't read " + selectedPak.ClickedItem.Text + " with this key", Color.FromArgb(255, 244, 66, 66), "Error");
            }
            if (loadAllPaKs)
            {
                //ADD TO DICTIONNARY
                RegisterPaKsinDict(_paksArray, null, true);

                if (!File.Exists(App.DefaultOutputPath + "\\FortnitePAKs.txt"))
                {
                    UpdateConsole("Can't read .PAK files with this key", Color.FromArgb(255, 244, 66, 66), "Error");
                }
                else
                {
                    PakAsTxt = File.ReadAllLines(App.DefaultOutputPath + "\\FortnitePAKs.txt");
                    File.Delete(App.DefaultOutputPath + "\\FortnitePAKs.txt");

                    Invoke(new Action(() =>
                    {
                        treeView1.BeginUpdate();
                        for (int i = 0; i < PakAsTxt.Length; i++)
                        {
                            TreeParsePath(treeView1.Nodes, PakAsTxt[i].Replace(PakAsTxt[i].Split('/').Last(), ""));
                        }
                        treeView1.EndUpdate();
                    }));
                    UpdateConsole(Settings.Default.PAKsPath, Color.FromArgb(255, 66, 244, 66), "Success");
                }
            }
            if (getDiff)
            {
                //ADD TO DICTIONNARY
                RegisterPaKsinDict(_paksArray, null, true);

                if (!File.Exists(App.DefaultOutputPath + "\\FortnitePAKs.txt"))
                {
                    UpdateConsole("Can't read .PAK files with this key", Color.FromArgb(255, 244, 66, 66), "Error");
                }
                else
                {
                    UpdateConsole("Comparing files...", Color.FromArgb(255, 244, 132, 66), "Loading");
                    ComparePaKs();
                    if (updateMode)
                    {
                        UmFilter(PakAsTxt, _diffToExtract);
                        Checking.UmWorking = true;
                    }

                    Invoke(new Action(() =>
                    {
                        treeView1.BeginUpdate();
                        for (int i = 0; i < PakAsTxt.Length; i++)
                        {
                            TreeParsePath(treeView1.Nodes, PakAsTxt[i].Replace(PakAsTxt[i].Split('/').Last(), ""));
                        }
                        treeView1.EndUpdate();
                    }));
                    UpdateConsole("Files compared", Color.FromArgb(255, 66, 244, 66), "Success");
                }
            }
        }
        private void CreateBackupList(string[] allYourPaKs)
        {
            _backupDynamicKeys = null;

            bool connection = IsInternetAvailable();
            if (connection && (!string.IsNullOrWhiteSpace(Settings.Default.eEmail) || !string.IsNullOrWhiteSpace(Settings.Default.ePassword)))
            {
                string myContent = DynamicPAKs.GetEndpoint("https://fortnite-public-service-prod11.ol.epicgames.com/fortnite/api/storefront/v2/keychain", true);

                if (myContent.Contains("\"errorCode\": \"errors.com.epicgames.common.authentication.authentication_failed\""))
                    AppendText("EPIC Authentication Failed.", Color.Red, true);
                else
                    _backupDynamicKeys = AesKeyParser.FromJson(myContent);
            }

            Settings.Default.AESKey = AESKeyTextBox.Text.Substring(2).ToUpper();
            Settings.Default.Save();

            for (int i = 0; i < allYourPaKs.Length; i++)
            {
                string arCurrentUsedPak = allYourPaKs[i]; //SET CURRENT PAK
                string arCurrentUsedPakGuid = ThePak.ReadPakGuid(Settings.Default.PAKsPath + "\\" + arCurrentUsedPak); //SET CURRENT PAK GUID

                if (arCurrentUsedPakGuid == "0-0-0-0") //NO DYNAMIC PAK
                {
                    try
                    {
                        JohnWick.MyExtractor = new PakExtractor(Settings.Default.PAKsPath + "\\" + arCurrentUsedPak, Settings.Default.AESKey);
                    }
                    catch (Exception)
                    {
                        break;
                    }

                    if (JohnWick.MyExtractor.GetFileList() != null)
                    {
                        if (!File.Exists(App.DefaultOutputPath + "\\Backup" + _backupFileName))
                            File.Create(App.DefaultOutputPath + "\\Backup" + _backupFileName).Dispose();

                        string[] CurrentUsedPakLines = JohnWick.MyExtractor.GetFileList().ToArray();
                        for (int ii = 0; ii < CurrentUsedPakLines.Length; ii++)
                        {
                            CurrentUsedPakLines[ii] = JohnWick.MyExtractor.GetMountPoint().Substring(6) + CurrentUsedPakLines[ii];
                        }
                        UpdateConsole(".PAK mount point: " + JohnWick.MyExtractor.GetMountPoint().Substring(9), Color.FromArgb(255, 244, 132, 66), "Waiting");

                        File.AppendAllLines(App.DefaultOutputPath + "\\Backup" + _backupFileName, CurrentUsedPakLines);
                    }
                }
                else if (_backupDynamicKeys != null)
                {
                    string oldGuid = string.Empty;
                    foreach (string myString in _backupDynamicKeys)
                    {
                        string[] parts = myString.Split(':');
                        string newGuid = DynamicPAKs.getPakGuidFromKeychain(parts);

                        /***
                         * if same guid several time in keychain do not backup twice
                         * it works fine that way because of the loop through all the paks
                         * even if in keychain we do "found 1004" -> "found 1001" -> "found 1004" through the paks we do 1000 -> 1001 -> 1002...
                        ***/
                        if (newGuid == arCurrentUsedPakGuid && oldGuid != newGuid)
                        {
                            byte[] bytes = Convert.FromBase64String(parts[1]);
                            string aeskey = BitConverter.ToString(bytes).Replace("-", "");
                            oldGuid = newGuid;

                            try
                            {
                                JohnWick.MyExtractor = new PakExtractor(Settings.Default.PAKsPath + "\\" + arCurrentUsedPak, aeskey);
                            }
                            catch (Exception)
                            {
                                continue;
                            }

                            if (JohnWick.MyExtractor.GetFileList() != null)
                            {
                                if (!File.Exists(App.DefaultOutputPath + "\\Backup" + _backupFileName))
                                    File.Create(App.DefaultOutputPath + "\\Backup" + _backupFileName).Dispose();

                                string[] CurrentUsedPakLines = JohnWick.MyExtractor.GetFileList().ToArray();
                                for (int ii = 0; ii < CurrentUsedPakLines.Length; ii++)
                                {
                                    CurrentUsedPakLines[ii] = JohnWick.MyExtractor.GetMountPoint().Substring(6) + CurrentUsedPakLines[ii];
                                }
                                AppendText("Backing up ", Color.Black);
                                AppendText(arCurrentUsedPak, Color.DarkRed, true);

                                File.AppendAllLines(App.DefaultOutputPath + "\\Backup" + _backupFileName, CurrentUsedPakLines);
                            }
                        }
                    }
                }
            }

            if (File.Exists(App.DefaultOutputPath + "\\Backup" + _backupFileName))
                UpdateConsole("\\Backup" + _backupFileName + " successfully created", Color.FromArgb(255, 66, 244, 66), "Success");
            else
                UpdateConsole("Can't create " + _backupFileName.Substring(1), Color.FromArgb(255, 244, 66, 66), "Error");
        }
        private void UpdateModeExtractSave()
        {
            CreatePakList(null, false, true, true);

            _questStageDict = new Dictionary<string, long>();
            Invoke(new Action(() =>
            {
                ExtractButton.Enabled = false;
                OpenImageButton.Enabled = false;
                StopButton.Enabled = true;
            }));
            if (backgroundWorker2.IsBusy != true)
            {
                backgroundWorker2.RunWorkerAsync();
            }
        }
        private void UmFilter(String[] theFile, Dictionary<string, string> diffToExtract)
        {
            List<string> searchResults = new List<string>();

            if (Settings.Default.UMCosmetics)
                searchResults.Add("Athena/Items/Cosmetics/");
            if (Settings.Default.UMVariants)
                searchResults.Add("Athena/Items/CosmeticVariantTokens/");
            if (Settings.Default.UMConsumablesWeapons)
            {
                searchResults.Add("AGID_");
                searchResults.Add("WID_");
            }
            if (Settings.Default.UMTraps)
                searchResults.Add("Athena/Items/Traps/");
            if (Settings.Default.UMChallenges)
                searchResults.Add("Athena/Items/ChallengeBundles/");

            if (Settings.Default.UMTCosmeticsVariants)
            {
                searchResults.Add("UI/Foundation/Textures/Icons/Backpacks/");
                searchResults.Add("UI/Foundation/Textures/Icons/Emotes/");
                searchResults.Add("UI/Foundation/Textures/Icons/Heroes/Athena/Soldier/");
                searchResults.Add("UI/Foundation/Textures/Icons/Heroes/Variants/");
                searchResults.Add("UI/Foundation/Textures/Icons/Skydiving/");
                searchResults.Add("UI/Foundation/Textures/Icons/Pets/");
                searchResults.Add("UI/Foundation/Textures/Icons/Wraps/");
            }
            if (Settings.Default.UMTLoading)
            {
                searchResults.Add("FortniteGame/Content/2dAssets/Loadingscreens/");
                searchResults.Add("UI/Foundation/Textures/LoadingScreens/");
            }
            if (Settings.Default.UMTWeapons)
                searchResults.Add("UI/Foundation/Textures/Icons/Weapons/Items/");
            if (Settings.Default.UMTBanners)
            {
                searchResults.Add("FortniteGame/Content/2dAssets/Banners/");
                searchResults.Add("UI/Foundation/Textures/Banner/");
                searchResults.Add("FortniteGame/Content/2dAssets/Sprays/");
                searchResults.Add("FortniteGame/Content/2dAssets/Emoji/");
                searchResults.Add("FortniteGame/Content/2dAssets/Music/");
                searchResults.Add("FortniteGame/Content/2dAssets/Toys/");
            }
            if (Settings.Default.UMTFeaturedIMGs)
                searchResults.Add("UI/Foundation/Textures/BattleRoyale/");
            if (Settings.Default.UMTAthena)
                searchResults.Add("UI/Foundation/Textures/Icons/Athena/");
            if (Settings.Default.UMTAthena)
                searchResults.Add("UI/Foundation/Textures/Icons/Athena/");
            if (Settings.Default.UMTDevices)
                searchResults.Add("UI/Foundation/Textures/Icons/Devices/");
            if (Settings.Default.UMTVehicles)
                searchResults.Add("UI/Foundation/Textures/Icons/Vehicles/");

            for (int i = 0; i < theFile.Length; i++)
            {
                bool b = searchResults.Any(s => theFile[i].Contains(s));
                if (b)
                {
                    string filename = theFile[i].Substring(theFile[i].LastIndexOf("/", StringComparison.Ordinal) + 1);
                    if (filename.Contains(".uasset") || filename.Contains(".uexp") || filename.Contains(".ubulk"))
                    {
                        if (!diffToExtract.ContainsKey(filename.Substring(0, filename.LastIndexOf(".", StringComparison.Ordinal))))
                            diffToExtract.Add(filename.Substring(0, filename.LastIndexOf(".", StringComparison.Ordinal)), theFile[i]);
                    }
                }
            }
        }

        //EVENTS
        private async void loadOneToolStripMenuItem_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            await Task.Run(() => {
                Invoke(new Action(() =>
                {
                    scintilla1.Text = "";
                    pictureBox1.Image = null;

                    treeView1.Nodes.Clear(); //SMH HERE IT DOESN'T LAG
                    listBox1.Items.Clear();
                }));

                CreatePakList(e);
            });
        }
        private async void loadAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Invoke(new Action(() =>
            {
                scintilla1.Text = "";
                pictureBox1.Image = null;

                treeView1.Nodes.Clear(); //SMH HERE IT DOESN'T LAG
                listBox1.Items.Clear();
            }));

            if (differenceModeToolStripMenuItem.Checked == false)
            {
                await Task.Run(() => {
                    CreatePakList(null, true);
                });
            }
            if (differenceModeToolStripMenuItem.Checked && updateModeToolStripMenuItem.Checked == false)
            {
                await Task.Run(() => {
                    CreatePakList(null, false, true);
                });
            }
            if (differenceModeToolStripMenuItem.Checked && updateModeToolStripMenuItem.Checked)
            {
                await Task.Run(() => {
                    UpdateModeExtractSave();
                });
            }
        }
        private async void backupPAKsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await Task.Run(() => {
                CreateBackupList(_paksArray);
            });
        }
        //UPDATE MODE
        private void backgroundWorker2_DoWork(object sender, DoWorkEventArgs e)
        {
            StopWatch = new Stopwatch();
            StopWatch.Start();
            Utilities.CreateDefaultFolders();
            ExtractAndSerializeItems(e, true);
        }
        private void backgroundWorker2_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            StopWatch.Stop();
            if (e.Cancelled)
            {
                UpdateConsole("Canceled!", Color.FromArgb(255, 244, 66, 66), "Error");
            }
            else if (e.Error != null)
            {
                UpdateConsole(e.Error.Message, Color.FromArgb(255, 244, 66, 66), "Error");
            }
            else if (Checking.UmWorking == false)
            {
                UpdateConsole("Can't read .PAK files with this key", Color.FromArgb(255, 244, 66, 66), "Error");
            }
            else
            {
                TimeSpan ts = StopWatch.Elapsed;
                string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
                UpdateConsole("Time elapsed: " + elapsedTime, Color.FromArgb(255, 66, 244, 66), "Success");
            }

            SelectedItemsArray = null;
            Checking.UmWorking = false;
            Invoke(new Action(() =>
            {
                updateModeToolStripMenuItem.Checked = false;
                StopButton.Enabled = false;
                OpenImageButton.Enabled = true;
                ExtractButton.Enabled = true;
            }));
        }
        #endregion

        #region FILL LISTBOX & FILTER
        //METHODS
        public static IEnumerable<TItem> GetAncestors<TItem>(TItem item, Func<TItem, TItem> getParentFunc)
        {
            if (getParentFunc == null)
            {
                throw new ArgumentNullException("getParentFunc");
            }
            if (ReferenceEquals(item, null)) yield break;
            for (TItem curItem = getParentFunc(item); !ReferenceEquals(curItem, null); curItem = getParentFunc(curItem))
            {
                yield return curItem;
            }
        }
        private void GetFilesAndFill(TreeNodeMouseClickEventArgs selectedNode)
        {
            List<string> itemsNotToDisplay = new List<string>();
            _itemsToDisplay = new List<string>();

            Invoke(new Action(() =>
            {
                listBox1.Items.Clear();
                FilterTextBox.Text = string.Empty;
            }));

            var all = GetAncestors(selectedNode.Node, x => x.Parent).ToList();
            all.Reverse();
            var full = string.Join("/", all.Select(x => x.Text)) + "/" + selectedNode.Node.Text + "/";
            if (string.IsNullOrEmpty(full))
            {
                return;
            }

            var dirfiles = PakAsTxt.Where(x => x.StartsWith(full) && !x.Replace(full, "").Contains("/"));
            var enumerable = dirfiles as string[] ?? dirfiles.ToArray();
            if (!enumerable.Any())
            {
                return;
            }

            foreach (var i in enumerable)
            {
                string v;
                if (i.Contains(".uasset") || i.Contains(".uexp") || i.Contains(".ubulk"))
                {
                    v = i.Substring(0, i.LastIndexOf('.'));
                }
                else
                {
                    v = i.Replace(full, "");
                }
                itemsNotToDisplay.Add(v.Replace(full, ""));
            }
            _itemsToDisplay = itemsNotToDisplay.Distinct().ToList(); //NO DUPLICATION + NO EXTENSION = EASY TO FIND WHAT WE WANT
            Invoke(new Action(() =>
            {
                for (int i = 0; i < _itemsToDisplay.Count; i++)
                {
                    listBox1.Items.Add(_itemsToDisplay[i]);
                }
                ExtractButton.Enabled = listBox1.SelectedIndex >= 0; //DISABLE EXTRACT BUTTON IF NOTHING IS SELECTED IN LISTBOX
            }));
        }
        public static bool CaseInsensitiveContains(string text, string value, StringComparison stringComparison = StringComparison.CurrentCultureIgnoreCase)
        {
            return text.IndexOf(value, stringComparison) >= 0;
        } //FILTER INSENSITIVE
        private void FilterItems()
        {
            if (listBox1.InvokeRequired)
            {
                listBox1.Invoke(new Action(FilterItems));
                return;
            }

            listBox1.BeginUpdate();
            listBox1.Items.Clear();

            if (_itemsToDisplay != null)
            {
                if (!string.IsNullOrEmpty(FilterTextBox.Text))
                {
                    for (int i = 0; i < _itemsToDisplay.Count; i++)
                    {
                        if (CaseInsensitiveContains(_itemsToDisplay[i], FilterTextBox.Text))
                        {
                            listBox1.Items.Add(_itemsToDisplay[i]);
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < _itemsToDisplay.Count; i++)
                    {
                        listBox1.Items.Add(_itemsToDisplay[i]);
                    }
                }
            }

            listBox1.EndUpdate();
        }
        public async void ExpandMyLitleBoys(TreeNode node, List<string> path)
        {
            path.RemoveAt(0);
            node.Expand();

            if (path.Count == 0)
                return;

            if (path.Count == 1)
            {
                treeView1.SelectedNode = node;
                await Task.Run(() => {
                    List<string> itemsNotToDisplay = new List<string>();
                    _itemsToDisplay = new List<string>();

                    Invoke(new Action(() =>
                    {
                        listBox1.Items.Clear();
                        FilterTextBox.Text = string.Empty;
                    }));

                    var all = GetAncestors(node, x => x.Parent).ToList();
                    all.Reverse();
                    var full = string.Join("/", all.Select(x => x.Text)) + "/" + node.Text + "/";
                    if (string.IsNullOrEmpty(full))
                    {
                        return;
                    }

                    var dirfiles = PakAsTxt.Where(x => x.StartsWith(full) && !x.Replace(full, "").Contains("/"));
                    var enumerable = dirfiles as string[] ?? dirfiles.ToArray();
                    if (!enumerable.Any())
                    {
                        return;
                    }

                    foreach (var i in enumerable)
                    {
                        string v;
                        if (i.Contains(".uasset") || i.Contains(".uexp") || i.Contains(".ubulk"))
                        {
                            v = i.Substring(0, i.LastIndexOf('.'));
                        }
                        else
                        {
                            v = i.Replace(full, "");
                        }
                        itemsNotToDisplay.Add(v.Replace(full, ""));
                    }
                    _itemsToDisplay = itemsNotToDisplay.Distinct().ToList(); //NO DUPLICATION + NO EXTENSION = EASY TO FIND WHAT WE WANT
                    Invoke(new Action(() =>
                    {
                        for (int i = 0; i < _itemsToDisplay.Count; i++)
                        {
                            listBox1.Items.Add(_itemsToDisplay[i]);
                        }
                        ExtractButton.Enabled = listBox1.SelectedIndex >= 0; //DISABLE EXTRACT BUTTON IF NOTHING IS SELECTED IN LISTBOX
                    }));
                });
                for (int i = 0; i < listBox1.Items.Count; i++)
                {
                    if (listBox1.Items[i].ToString() == SearchFiles.SfPath.Substring(SearchFiles.SfPath.LastIndexOf("/", StringComparison.Ordinal) + 1))
                    {
                        listBox1.SelectedItem = listBox1.Items[i];
                    }
                }
            }

            foreach (TreeNode mynode in node.Nodes)
                if (mynode.Text == path[0])
                {
                    ExpandMyLitleBoys(mynode, path); //recursive call
                    break;
                }
        }
        public void OpenMe()
        {
            if (SearchFiles.IsClosed)
            {
                treeView1.CollapseAll();
                var pathList = SearchFiles.SfPath.Split('/').ToList();
                foreach (TreeNode node in treeView1.Nodes)
                    if (node.Text == pathList[0])
                        ExpandMyLitleBoys(node, pathList);
            }
        }

        //EVENTS
        private async void treeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            await Task.Run(() => {
                GetFilesAndFill(e);
            });
        }
        private async void FilterTextBox_TextChanged(object sender, EventArgs e)
        {
            await Task.Run(() => {
                FilterItems();
            });
        }
        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBox1.SelectedItem != null && SelectedItemsArray == null)
            {
                ExtractButton.Enabled = true;
            }
        }
        #endregion

        #region EXTRACT BUTTON
        //METHODS
        private void ExtractAndSerializeItems(DoWorkEventArgs e, bool updateMode = false)
        {
            if (updateMode == false)
            {
                //REGISTER SELECTED ITEMS
                Invoke(new Action(() =>
                {
                    SelectedItemsArray = new string[listBox1.SelectedItems.Count];
                    for (int i = 0; i < listBox1.SelectedItems.Count; i++) //ADD SELECTED ITEM TO ARRAY
                    {
                        SelectedItemsArray[i] = listBox1.SelectedItems[i].ToString();
                    }
                }));
            }
            else
            {
                //REGISTER SELECTED ITEMS
                Invoke(new Action(() =>
                {
                    SelectedItemsArray = new string[_diffToExtract.Count];
                    for (int i = 0; i < _diffToExtract.Count; i++) //ADD DICT ITEM TO ARRAY
                    {
                        SelectedItemsArray[i] = _diffToExtract.Keys.ElementAt(i);
                    }
                }));
            }

            //DO WORK
            for (int i = 0; i < SelectedItemsArray.Length; i++)
            {
                if (backgroundWorker1.CancellationPending && backgroundWorker1.IsBusy)
                {
                    e.Cancel = true;
                    return;
                }
                if (backgroundWorker2.CancellationPending && backgroundWorker2.IsBusy)
                {
                    e.Cancel = true;
                    return;
                }

                ThePak.CurrentUsedItem = SelectedItemsArray[i];

                if (ThePak.CurrentUsedPakGuid != null && ThePak.CurrentUsedPakGuid != "0-0-0-0")
                    ExtractedFilePath = JohnWick.ExtractAsset(ThePak.CurrentUsedPak, ThePak.CurrentUsedItem);
                else
                    ExtractedFilePath = JohnWick.ExtractAsset(ThePak.AllpaksDictionary[ThePak.CurrentUsedItem], ThePak.CurrentUsedItem);

                if (ExtractedFilePath != null)
                {
                    UpdateConsole(ThePak.CurrentUsedItem + " successfully extracted", Color.FromArgb(255, 66, 244, 66), "Success");
                    if (ExtractedFilePath.Contains(".uasset") || ExtractedFilePath.Contains(".uexp") || ExtractedFilePath.Contains(".ubulk"))
                    {
                        JohnWick.MyAsset = new PakAsset(ExtractedFilePath.Substring(0, ExtractedFilePath.LastIndexOf('.')));
                        JsonParseFile();
                    }
                    if (ExtractedFilePath.Contains(".ufont"))
                        ConvertToTtf(ExtractedFilePath);
                    if (ExtractedFilePath.Contains(".ini"))
                    {
                        Invoke(new Action(() =>
                        {
                            scintilla1.Text = File.ReadAllText(ExtractedFilePath);
                        }));
                    }
                }
                else
                    UpdateConsole("Error while extracting " + ThePak.CurrentUsedItem, Color.FromArgb(255, 244, 66, 66), "Error");
            }
        }
        private void JsonParseFile()
        {
            if (JohnWick.MyAsset.GetSerialized() != null)
            {
                UpdateConsole(ThePak.CurrentUsedItem + " successfully serialized", Color.FromArgb(255, 66, 244, 66), "Success");

                Invoke(new Action(() =>
                {
                    try
                    {
                        scintilla1.Text = JToken.Parse(JohnWick.MyAsset.GetSerialized()).ToString();
                    }
                    catch (JsonReaderException)
                    {
                        AppendText(ThePak.CurrentUsedItem + " ", Color.Red);
                        AppendText(".JSON file can't be displayed", Color.Black, true);
                    }
                }));

                NavigateThroughJson(JohnWick.MyAsset, ExtractedFilePath);
            }
            else
                UpdateConsole("No serialized file found", Color.FromArgb(255, 244, 66, 66), "Error");
        }
        private void NavigateThroughJson(PakAsset theAsset, string questJson = null)
        {
            try
            {
                string parsedJson = JToken.Parse(theAsset.GetSerialized()).ToString();
                var itemId = ItemsIdParser.FromJson(parsedJson);

                UpdateConsole("Parsing " + ThePak.CurrentUsedItem + "...", Color.FromArgb(255, 244, 132, 66), "Waiting");
                for (int i = 0; i < itemId.Length; i++)
                {
                    if (Settings.Default.createIconForCosmetics && itemId[i].ExportType.Contains("Athena") && itemId[i].ExportType.Contains("Item") && itemId[i].ExportType.Contains("Definition"))
                    {
                        pictureBox1.Image = CreateItemIcon(itemId[i], "athIteDef");
                    }
                    else if (Settings.Default.createIconForConsumablesWeapons && (itemId[i].ExportType == "FortWeaponRangedItemDefinition" || itemId[i].ExportType == "FortWeaponMeleeItemDefinition"))
                    {
                        pictureBox1.Image = CreateItemIcon(itemId[i], "consAndWeap");
                    }
                    else if (Settings.Default.createIconForTraps && (itemId[i].ExportType == "FortTrapItemDefinition" || itemId[i].ExportType == "FortContextTrapItemDefinition"))
                    {
                        pictureBox1.Image = CreateItemIcon(itemId[i]);
                    }
                    else if (Settings.Default.createIconForVariants && (itemId[i].ExportType == "FortVariantTokenType"))
                    {
                        pictureBox1.Image = CreateItemIcon(itemId[i], "variant");
                    }
                    else if (Settings.Default.createIconForAmmo && (itemId[i].ExportType == "FortAmmoItemDefinition"))
                    {
                        pictureBox1.Image = CreateItemIcon(itemId[i], "ammo");
                    }
                    else if (questJson != null && (Settings.Default.createIconForSTWHeroes && (itemId[i].ExportType == "FortHeroType" && (questJson.Contains("ItemDefinition") || questJson.Contains("TestDefsSkydive") || questJson.Contains("GameplayPrototypes"))))) //Contains x not to trigger HID from BR
                    {
                        pictureBox1.Image = CreateItemIcon(itemId[i], "stwHeroes");
                    }
                    else if (Settings.Default.createIconForSTWDefenders && (itemId[i].ExportType == "FortDefenderItemDefinition"))
                    {
                        pictureBox1.Image = CreateItemIcon(itemId[i], "stwDefenders");
                    }
                    else if (Settings.Default.createIconForSTWCardPacks && (itemId[i].ExportType == "FortCardPackItemDefinition"))
                    {
                        pictureBox1.Image = CreateItemIcon(itemId[i]);
                    }
                    else if (itemId[i].ExportType == "FortChallengeBundleItemDefinition")
                    {
                        CreateBundleChallengesIcon(itemId[i], parsedJson, questJson);
                    }
                    else if (itemId[i].ExportType == "Texture2D") { ConvertTexture2D(); }
                    else if (itemId[i].ExportType == "SoundWave") { ConvertSoundWave(); }
                    else { UpdateConsole(ThePak.CurrentUsedItem + " successfully extracted", Color.FromArgb(255, 66, 244, 66), "Success"); }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        private Bitmap CreateItemIcon(ItemsIdParser theItem, string specialMode = null)
        {
            UpdateConsole(ThePak.CurrentUsedItem + " is an Item Definition", Color.FromArgb(255, 66, 244, 66), "Success");

            Bitmap bmp = new Bitmap(522, 522);
            Graphics g = Graphics.FromImage(bmp);
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            Rarity.DrawRarity(theItem, g, specialMode);

            ItemIcon.ItemIconPath = string.Empty;
            ItemIcon.GetItemIcon(theItem, Settings.Default.loadFeaturedImage);
            if (File.Exists(ItemIcon.ItemIconPath))
            {
                Image itemIcon;
                using (var bmpTemp = new Bitmap(ItemIcon.ItemIconPath))
                {
                    itemIcon = new Bitmap(bmpTemp);
                }
                g.DrawImage(ImageUtilities.ResizeImage(itemIcon, 512, 512), new Point(5, 5));
            }
            else
            {
                Image itemIcon = Resources.unknown512;
                g.DrawImage(itemIcon, new Point(0, 0));
            }

            ItemIcon.DrawWatermark(g);

            Image bg512 = Resources.BG512;
            g.DrawImage(bg512, new Point(5, 383));

            #region DRAW TEXT
            try
            {
                g.DrawString(theItem.DisplayName, new Font(FontUtilities.pfc.Families[0], 35), new SolidBrush(Color.White), new Point(522 / 2, 395), FontUtilities.centeredString);
            }
            catch (NullReferenceException)
            {
                AppendText(ThePak.CurrentUsedItem + " ", Color.Red);
                AppendText("No ", Color.Black);
                AppendText("DisplayName ", Color.SteelBlue);
                AppendText("found", Color.Black, true);
            } //NAME
            try
            {
                g.DrawString(theItem.Description, new Font("Arial", 10), new SolidBrush(Color.White), new RectangleF(5, 441, 512, 49), FontUtilities.centeredStringLine);
            }
            catch (NullReferenceException)
            {
                AppendText(ThePak.CurrentUsedItem + " ", Color.Red);
                AppendText("No ", Color.Black);
                AppendText("Description ", Color.SteelBlue);
                AppendText("found", Color.Black, true);
            } //DESCRIPTION
            if (specialMode == "athIteDef")
            {
                try
                {
                    g.DrawString(theItem.ShortDescription, new Font(FontUtilities.pfc.Families[0], 13), new SolidBrush(Color.White), new Point(5, 500));
                }
                catch (NullReferenceException)
                {
                    AppendText(ThePak.CurrentUsedItem + " ", Color.Red);
                    AppendText("No ", Color.Black);
                    AppendText("ShortDescription ", Color.SteelBlue);
                    AppendText("found", Color.Black, true);
                } //TYPE
                try
                {
                    g.DrawString(theItem.GameplayTags.GameplayTagsGameplayTags[Array.FindIndex(theItem.GameplayTags.GameplayTagsGameplayTags, x => x.StartsWith("Cosmetics.Source."))].Substring(17), new Font(FontUtilities.pfc.Families[0], 13), new SolidBrush(Color.White), new Point(522 - 5, 500), FontUtilities.rightString);
                }
                catch (NullReferenceException)
                {
                    AppendText(ThePak.CurrentUsedItem + " ", Color.Red);
                    AppendText("No ", Color.Black);
                    AppendText("GameplayTags ", Color.SteelBlue);
                    AppendText("found", Color.Black, true);
                }
                catch (IndexOutOfRangeException)
                {
                    AppendText(ThePak.CurrentUsedItem + " ", Color.Red);
                    AppendText("No ", Color.Black);
                    AppendText("GameplayTags ", Color.SteelBlue);
                    AppendText("as ", Color.Black);
                    AppendText("Cosmetics.Source ", Color.SteelBlue);
                    AppendText("found", Color.Black, true);
                } //COSMETIC SOURCE
                if (theItem.ExportType == "AthenaItemWrapDefinition" && Checking.WasFeatured && ItemIcon.ItemIconPath.Contains("WeaponRenders")) //ONLY TRIGGER IF THE WEAPON RENDER IS TRIGGERED
                {
                    string wrapAddImg = theItem.LargePreviewImage.AssetPathName.Substring(theItem.LargePreviewImage.AssetPathName.LastIndexOf(".", StringComparison.Ordinal) + 1);

                    UpdateConsole("Additional image " + wrapAddImg, Color.FromArgb(255, 244, 132, 66), "Waiting");
                    ItemIcon.ItemIconPath = JohnWick.AssetToTexture2D(wrapAddImg);

                    if (File.Exists(ItemIcon.ItemIconPath))
                    {
                        Image itemIcon;
                        using (var bmpTemp = new Bitmap(ItemIcon.ItemIconPath))
                        {
                            itemIcon = new Bitmap(bmpTemp);
                        }
                        g.DrawImage(ImageUtilities.ResizeImage(itemIcon, 122, 122), new Point(395, 282));
                    }
                }
            }
            if (specialMode == "consAndWeap")
            {
                try
                {
                    g.DrawString(theItem.GameplayTags.GameplayTagsGameplayTags[Array.FindIndex(theItem.GameplayTags.GameplayTagsGameplayTags, x => x.StartsWith("Athena.ItemAction."))].Substring(18), new Font(FontUtilities.pfc.Families[0], 13), new SolidBrush(Color.White), new Point(522 - 5, 500), FontUtilities.rightString);
                }
                catch (NullReferenceException)
                {
                    AppendText(ThePak.CurrentUsedItem + " ", Color.Red);
                    AppendText("No ", Color.Black);
                    AppendText("GameplayTags ", Color.SteelBlue);
                    AppendText("found", Color.Black, true);
                }
                catch (IndexOutOfRangeException)
                {
                    try
                    {
                        g.DrawString(theItem.GameplayTags.GameplayTagsGameplayTags[Array.FindIndex(theItem.GameplayTags.GameplayTagsGameplayTags, x => x.StartsWith("Weapon."))].Substring(7), new Font(FontUtilities.pfc.Families[0], 13), new SolidBrush(Color.White), new Point(522 - 5, 500), FontUtilities.rightString);
                    }
                    catch (NullReferenceException)
                    {
                        AppendText(ThePak.CurrentUsedItem + " ", Color.Red);
                        AppendText("No ", Color.Black);
                        AppendText("GameplayTags ", Color.SteelBlue);
                        AppendText("found", Color.Black, true);
                    }
                    catch (IndexOutOfRangeException)
                    {
                        AppendText(ThePak.CurrentUsedItem + " ", Color.Red);
                        AppendText("No ", Color.Black);
                        AppendText("GameplayTags ", Color.SteelBlue);
                        AppendText("as ", Color.Black);
                        AppendText("Athena.ItemAction ", Color.SteelBlue);
                        AppendText("or ", Color.Black);
                        AppendText("Weapon ", Color.SteelBlue);
                        AppendText("found", Color.Black, true);
                    }
                } //ACTION
                if (ExtractedFilePath.Contains("Items\\Consumables"))
                {
                    try
                    {
                        g.DrawString("Max Stack Size: " + theItem.MaxStackSize, new Font(FontUtilities.pfc.Families[0], 13), new SolidBrush(Color.White), new Point(5, 500));
                    }
                    catch (NullReferenceException)
                    {
                        AppendText(ThePak.CurrentUsedItem + " ", Color.Red);
                        AppendText("No ", Color.Black);
                        AppendText("MaxStackSize ", Color.SteelBlue);
                        AppendText("found", Color.Black, true);
                    } //MAX STACK SIZE
                }
                if (theItem.AmmoData != null && theItem.AmmoData.AssetPathName.Contains("Ammo")) //TO AVOID TRIGGERING CONSUMABLES, NAME SHOULD CONTAIN "AMMO"
                {
                    ItemIcon.GetAmmoData(theItem.AmmoData.AssetPathName, g);
                }
            }
            if (specialMode == "variant")
            {
                try
                {
                    g.DrawString(theItem.ShortDescription, new Font(FontUtilities.pfc.Families[0], 13), new SolidBrush(Color.White), new Point(5, 500));
                }
                catch (NullReferenceException)
                {
                    AppendText(ThePak.CurrentUsedItem + " ", Color.Red);
                    AppendText("No ", Color.Black);
                    AppendText("ShortDescription ", Color.SteelBlue);
                    AppendText("found", Color.Black, true);
                } //TYPE
                try
                {
                    g.DrawString(theItem.CosmeticItem, new Font(FontUtilities.pfc.Families[0], 13), new SolidBrush(Color.White), new Point(522 - 5, 500), FontUtilities.rightString);
                }
                catch (NullReferenceException)
                {
                    AppendText(ThePak.CurrentUsedItem + " ", Color.Red);
                    AppendText("No ", Color.Black);
                    AppendText("Cosmetic Item ", Color.SteelBlue);
                    AppendText("found", Color.Black, true);
                } //COSMETIC ITEM
            }
            try
            {
                if (theItem.GameplayTags.GameplayTagsGameplayTags[Array.FindIndex(theItem.GameplayTags.GameplayTagsGameplayTags, x => x.StartsWith("Cosmetics.UserFacingFlags."))].Contains("Animated"))
                {
                    Image animatedLogo = Resources.T_Icon_Animated_64;
                    g.DrawImage(ImageUtilities.ResizeImage(animatedLogo, 32, 32), new Point(6, -2));
                }
                else if (theItem.GameplayTags.GameplayTagsGameplayTags[Array.FindIndex(theItem.GameplayTags.GameplayTagsGameplayTags, x => x.StartsWith("Cosmetics.UserFacingFlags."))].Contains("HasUpgradeQuests") && theItem.ExportType != "AthenaPetCarrierItemDefinition")
                {
                    Image questLogo = Resources.T_Icon_Quests_64;
                    g.DrawImage(ImageUtilities.ResizeImage(questLogo, 32, 32), new Point(6, 6));
                }
                else if (theItem.GameplayTags.GameplayTagsGameplayTags[Array.FindIndex(theItem.GameplayTags.GameplayTagsGameplayTags, x => x.StartsWith("Cosmetics.UserFacingFlags."))].Contains("HasUpgradeQuests") && theItem.ExportType == "AthenaPetCarrierItemDefinition")
                {
                    Image petLogo = Resources.T_Icon_Pets_64;
                    g.DrawImage(ImageUtilities.ResizeImage(petLogo, 32, 32), new Point(6, 6));
                }
                else if (theItem.GameplayTags.GameplayTagsGameplayTags[Array.FindIndex(theItem.GameplayTags.GameplayTagsGameplayTags, x => x.StartsWith("Cosmetics.UserFacingFlags."))].Contains("HasVariants"))
                {
                    Image variantsLogo = Resources.T_Icon_Variant_64;
                    g.DrawImage(ImageUtilities.ResizeImage(variantsLogo, 32, 32), new Point(6, 6));
                }
                else if (theItem.GameplayTags.GameplayTagsGameplayTags[Array.FindIndex(theItem.GameplayTags.GameplayTagsGameplayTags, x => x.StartsWith("Cosmetics.UserFacingFlags."))].Contains("Reactive"))
                {
                    Image reactiveLogo = Resources.T_Icon_Adaptive_64;
                    g.DrawImage(ImageUtilities.ResizeImage(reactiveLogo, 32, 32), new Point(7, 7));
                }
                else if (theItem.GameplayTags.GameplayTagsGameplayTags[Array.FindIndex(theItem.GameplayTags.GameplayTagsGameplayTags, x => x.StartsWith("Cosmetics.UserFacingFlags."))].Contains("Traversal"))
                {
                    Image traversalLogo = Resources.T_Icon_Traversal_64;
                    g.DrawImage(ImageUtilities.ResizeImage(traversalLogo, 32, 32), new Point(6, 3));
                }
            }
            catch (Exception) { } //COSMETIC USER FACING FLAGS

            if (specialMode == "stwHeroes")
            {
                try
                {
                    g.DrawString(theItem.AttributeInitKey.AttributeInitCategory, new Font(FontUtilities.pfc.Families[0], 13), new SolidBrush(Color.White), new Point(522 - 5, 500), FontUtilities.rightString);
                }
                catch (NullReferenceException)
                {
                    AppendText(ThePak.CurrentUsedItem + " ", Color.Red);
                    AppendText("No ", Color.Black);
                    AppendText("AttributeInitCategory ", Color.SteelBlue);
                    AppendText("found", Color.Black, true);
                } //CHARACTER TYPE
                try
                {
                    g.DrawString("Power " + theItem.MinLevel + " to " + theItem.MaxLevel, new Font(FontUtilities.pfc.Families[0], 13), new SolidBrush(Color.White), new Point(5, 500));
                }
                catch (NullReferenceException)
                {
                    AppendText(ThePak.CurrentUsedItem + " ", Color.Red);
                    AppendText("No ", Color.Black);
                    AppendText("Level ", Color.SteelBlue);
                    AppendText("found", Color.Black, true);
                } //LEVEL
            }
            if (specialMode == "stwDefenders")
            {
                try
                {
                    g.DrawString(theItem.GameplayTags.GameplayTagsGameplayTags[Array.FindIndex(theItem.GameplayTags.GameplayTagsGameplayTags, x => x.StartsWith("NPC.CharacterType.Survivor.Defender."))].Substring(36), new Font(FontUtilities.pfc.Families[0], 13), new SolidBrush(Color.White), new Point(522 - 5, 500), FontUtilities.rightString);
                }
                catch (NullReferenceException)
                {
                    AppendText(ThePak.CurrentUsedItem + " ", Color.Red);
                    AppendText("No ", Color.Black);
                    AppendText("GameplayTags ", Color.SteelBlue);
                    AppendText("found", Color.Black, true);
                }
                catch (IndexOutOfRangeException)
                {
                    AppendText(ThePak.CurrentUsedItem + " ", Color.Red);
                    AppendText("No ", Color.Black);
                    AppendText("GameplayTags ", Color.SteelBlue);
                    AppendText("as ", Color.Black);
                    AppendText("NPC.CharacterType.Survivor.Defender ", Color.SteelBlue);
                    AppendText("found", Color.Black, true);
                } //CHARACTER TYPE
                try
                {
                    g.DrawString("Power " + theItem.MinLevel + " to " + theItem.MaxLevel, new Font(FontUtilities.pfc.Families[0], 13), new SolidBrush(Color.White), new Point(5, 500));
                }
                catch (NullReferenceException)
                {
                    AppendText(ThePak.CurrentUsedItem + " ", Color.Red);
                    AppendText("No ", Color.Black);
                    AppendText("Level ", Color.SteelBlue);
                    AppendText("found", Color.Black, true);
                } //LEVEL
            }
            #endregion

            UpdateConsole(theItem.DisplayName, Color.FromArgb(255, 66, 244, 66), "Success");
            if (autoSaveImagesToolStripMenuItem.Checked || updateModeToolStripMenuItem.Checked)
            {
                bmp.Save(App.DefaultOutputPath + "\\Icons\\" + ThePak.CurrentUsedItem + ".png", ImageFormat.Png);

                if (File.Exists(App.DefaultOutputPath + "\\Icons\\" + ThePak.CurrentUsedItem + ".png"))
                {
                    AppendText(ThePak.CurrentUsedItem, Color.DarkRed);
                    AppendText(" successfully saved", Color.Black, true);
                }
            }

            return bmp;
        }


        /// <summary>
        /// this is the main method that draw the bundle of challenges
        /// </summary>
        /// <param name="theItem"> needed for the DisplayName </param>
        /// <param name="theParsedJson"> to parse from this instead of calling MyAsset.GetSerialized() again </param>
        /// <param name="extractedBundlePath"> needed for the LastFolder </param>
        /// <returns> the bundle image ready to be displayed in pictureBox1 </returns>
        private void CreateBundleChallengesIcon(ItemsIdParser theItem, string theParsedJson, string extractedBundlePath)
        {
            ChallengeBundleIdParser bundleParser = ChallengeBundleIdParser.FromJson(theParsedJson).FirstOrDefault();
            BundleInfos.getBundleData(bundleParser);
            Bitmap bmp = null;

            if (Settings.Default.createIconForChallenges)
            {
                bmp = new Bitmap(2500, 5000);
                BundleDesign.BundlePath = extractedBundlePath;
                BundleDesign.theY = 275;
                BundleDesign.toDrawOn = Graphics.FromImage(bmp);
                BundleDesign.toDrawOn.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                BundleDesign.toDrawOn.SmoothingMode = SmoothingMode.HighQuality;
                BundleDesign.myItem = theItem;

                BundleDesign.drawBackground(bmp, bundleParser);
            }

            for (int i = 0; i < BundleInfos.BundleData.Count; i++)
            {
                AppendText(BundleInfos.BundleData[i].questDescr, Color.SteelBlue);
                AppendText("\t\tCount: " + BundleInfos.BundleData[i].questCount, Color.DarkRed);
                AppendText("\t\t" + BundleInfos.BundleData[i].rewardItemId + ":" + BundleInfos.BundleData[i].rewardItemQuantity, Color.DarkGreen, true);

                if (Settings.Default.createIconForChallenges)
                {
                    BundleDesign.theY += 140;

                    //draw quest description
                    BundleDesign.toDrawOn.DrawString(BundleInfos.BundleData[i].questDescr, new Font(FontUtilities.pfc.Families[1], 50), new SolidBrush(Color.White), new Point(100, BundleDesign.theY));

                    //draw slider + quest count
                    Image slider = Resources.Challenges_Slider;
                    BundleDesign.toDrawOn.DrawImage(slider, new Point(108, BundleDesign.theY + 86));
                    BundleDesign.toDrawOn.DrawString(BundleInfos.BundleData[i].questCount.ToString(), new Font(FontUtilities.pfc.Families[0], 20), new SolidBrush(Color.FromArgb(255, 255, 255, 255)), new Point(968, BundleDesign.theY + 87));

                    //draw quest reward
                    DrawingRewards.getRewards(BundleInfos.BundleData[i].rewardItemId, BundleInfos.BundleData[i].rewardItemQuantity);

                    if (i != 0)
                    {
                        //draw separator
                        BundleDesign.toDrawOn.DrawLine(new Pen(Color.FromArgb(30, 255, 255, 255)), 100, BundleDesign.theY - 10, 2410, BundleDesign.theY - 10);
                    }
                }
            }
            AppendText("", Color.Black, true);

            if (Settings.Default.createIconForChallenges)
            {
                BundleDesign.drawCompletionReward(bundleParser);
                BundleDesign.drawWatermark(bmp);

                //cut if too long and return the bitmap
                using (Bitmap bmp2 = bmp)
                {
                    var newImg = bmp2.Clone(
                        new Rectangle { X = 0, Y = 0, Width = bmp.Width, Height = BundleDesign.theY + 280 },
                        bmp2.PixelFormat);

                    pictureBox1.Image = newImg;
                }
            }

            UpdateConsole(theItem.DisplayName, Color.FromArgb(255, 66, 244, 66), "Success");
            if (autoSaveImagesToolStripMenuItem.Checked || updateModeToolStripMenuItem.Checked)
            {
                Invoke(new Action(() =>
                {
                    pictureBox1.Image.Save(App.DefaultOutputPath + "\\Icons\\" + ThePak.CurrentUsedItem + ".png", ImageFormat.Png);
                }));

                if (File.Exists(App.DefaultOutputPath + "\\Icons\\" + ThePak.CurrentUsedItem + ".png"))
                {
                    AppendText(ThePak.CurrentUsedItem, Color.DarkRed);
                    AppendText(" successfully saved", Color.Black, true);
                }
            }
        }

        private void ConvertTexture2D()
        {
            UpdateConsole(ThePak.CurrentUsedItem + " is a Texture2D", Color.FromArgb(255, 66, 244, 66), "Success");

            JohnWick.MyAsset = new PakAsset(ExtractedFilePath.Substring(0, ExtractedFilePath.LastIndexOf(".", StringComparison.Ordinal)));
            JohnWick.MyAsset.SaveTexture(ExtractedFilePath.Substring(0, ExtractedFilePath.LastIndexOf(".", StringComparison.Ordinal)) + ".png");
            string imgPath = ExtractedFilePath.Substring(0, ExtractedFilePath.LastIndexOf(".", StringComparison.Ordinal)) + ".png";

            if (File.Exists(imgPath))
            {
                pictureBox1.Image = Image.FromFile(imgPath);
            }

            if (autoSaveImagesToolStripMenuItem.Checked || updateModeToolStripMenuItem.Checked)
            {
                Invoke(new Action(() =>
                {
                    pictureBox1.Image.Save(App.DefaultOutputPath + "\\Icons\\" + ThePak.CurrentUsedItem + ".png", ImageFormat.Png);
                }));
                AppendText(ThePak.CurrentUsedItem, Color.DarkRed);
                AppendText(" successfully saved", Color.Black, true);
            }
        }
        private void ConvertSoundWave()
        {
            UpdateConsole(ThePak.CurrentUsedItem + " is a Sound", Color.FromArgb(255, 66, 244, 66), "Success");

            string soundPathToConvert = ExtractedFilePath.Substring(0, ExtractedFilePath.LastIndexOf('\\')) + "\\" + ThePak.CurrentUsedItem + ".uexp";
            string soundPathConverted = UnrealEngineDataToOgg.ConvertToOgg(soundPathToConvert);
            UpdateConsole("Converting " + ThePak.CurrentUsedItem, Color.FromArgb(255, 244, 132, 66), "Processing");

            if (File.Exists(soundPathConverted))
            {
                Utilities.OpenWithDefaultProgramAndNoFocus(soundPathConverted);
                UpdateConsole("Opening " + ThePak.CurrentUsedItem + ".ogg", Color.FromArgb(255, 66, 244, 66), "Success");
            }
            else
                UpdateConsole("Couldn't convert " + ThePak.CurrentUsedItem, Color.FromArgb(255, 244, 66, 66), "Error");
        }
        private void ConvertToTtf(string file)
        {
            if (File.Exists(file)) { File.Delete(file);  }

            File.Move(file, Path.ChangeExtension(file, ".ttf") ?? throw new InvalidOperationException());
            UpdateConsole(ThePak.CurrentUsedItem + " successfully converter to a font", Color.FromArgb(255, 66, 244, 66), "Success");
        }

        //EVENTS
        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            StopWatch = new Stopwatch();
            StopWatch.Start();
            Utilities.CreateDefaultFolders();
            ExtractAndSerializeItems(e);
        }
        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            StopWatch.Stop();
            if (e.Cancelled)
            {
                UpdateConsole("Canceled!", Color.FromArgb(255, 244, 66, 66), "Error");
            }
            else if (e.Error != null)
            {
                UpdateConsole(e.Error.Message, Color.FromArgb(255, 244, 66, 66), "Error");
            }
            else
            {
                TimeSpan ts = StopWatch.Elapsed;
                string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
                UpdateConsole("Time elapsed: " + elapsedTime, Color.FromArgb(255, 66, 244, 66), "Success");
            }

            SelectedItemsArray = null;
            Invoke(new Action(() =>
            {
                StopButton.Enabled = false;
                OpenImageButton.Enabled = true;
                ExtractButton.Enabled = true;
            }));
        }
        private void ExtractButton_Click(object sender, EventArgs e)
        {
            scintilla1.Text = "";
            pictureBox1.Image = null;
            _questStageDict = new Dictionary<string, long>();
            ExtractButton.Enabled = false;
            OpenImageButton.Enabled = false;
            StopButton.Enabled = true;

            if (backgroundWorker1.IsBusy != true)
            {
                backgroundWorker1.RunWorkerAsync();
            }
        }
        private void StopButton_Click(object sender, EventArgs e)
        {
            if (backgroundWorker1.WorkerSupportsCancellation)
            {
                backgroundWorker1.CancelAsync();
            }
            if (backgroundWorker2.WorkerSupportsCancellation)
            {
                backgroundWorker2.CancelAsync();
            }
        }
        #endregion

        #region IMAGES SAVE & MERGE
        //METHODS
        private void AskMergeImages()
        {
            if (string.IsNullOrEmpty(Settings.Default.mergerFileName))
            {
                MessageBox.Show(@"Please, set a name to your Merger file before trying to merge images

Steps:
	- Load
	- Settings", @"Merger File Name Missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                OpenFileDialog theDialog = new OpenFileDialog();
                theDialog.Multiselect = true;
                theDialog.InitialDirectory = App.DefaultOutputPath + "\\Icons\\";
                theDialog.Title = @"Choose your images";
                theDialog.Filter = @"PNG Files (*.png)|*.png|JPEG Files (*.jpg)|*.jpg|BMP Files (*.bmp)|*.bmp|All Files (*.*)|*.*";

                Invoke(new Action(() =>
                {
                    if (theDialog.ShowDialog() == DialogResult.OK)
                    {
                        List<Image> selectedImages = new List<Image>();
                        foreach (var files in theDialog.FileNames)
                        {
                            selectedImages.Add(Image.FromFile(files));
                        }

                        MergeSelected(selectedImages);
                    }
                }));
            }
        }
        private void MergeSelected(List<Image> mySelectedImages)
        {
            if (Settings.Default.mergerImagesRow == 0)
            {
                Settings.Default.mergerImagesRow = 7;
                Settings.Default.Save();
            }

            int numperrow = Settings.Default.mergerImagesRow;
            var w = 530 * numperrow;
            if (mySelectedImages.Count * 530 < 530 * numperrow)
            {
                w = mySelectedImages.Count * 530;
            }

            int h = int.Parse(Math.Ceiling(double.Parse(mySelectedImages.Count.ToString()) / numperrow).ToString(CultureInfo.InvariantCulture)) * 530;
            Bitmap bmp = new Bitmap(w - 8, h - 8);

            var num = 1;
            var curW = 0;
            var curH = 0;

            for (int i = 0; i < mySelectedImages.Count; i++)
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.DrawImage(ImageUtilities.ResizeImage(mySelectedImages[i], 522, 522), new PointF(curW, curH));
                    if (num % numperrow == 0)
                    {
                        curW = 0;
                        curH += 530;
                        num += 1;
                    }
                    else
                    {
                        curW += 530;
                        num += 1;
                    }
                }
            }
            bmp.Save(App.DefaultOutputPath + "\\" + Settings.Default.mergerFileName + ".png", ImageFormat.Png);

            OpenMerged(bmp);
        }
        private void OpenMerged(Bitmap mergedImage)
        {
            if (mergedImage != null)
            {
                var newForm = new Form();
                PictureBox pb = new PictureBox();
                pb.Dock = DockStyle.Fill;
                pb.Image = mergedImage;
                pb.SizeMode = PictureBoxSizeMode.Zoom;

                newForm.WindowState = FormWindowState.Maximized;
                newForm.Size = mergedImage.Size;
                newForm.Icon = Resources.FModel;
                newForm.Text = App.DefaultOutputPath + @"\" + Settings.Default.mergerFileName + @".png";
                newForm.StartPosition = FormStartPosition.CenterScreen;
                newForm.Controls.Add(pb);
                newForm.Show();
            }
        }

        //EVENTS
        private void OpenImageButton_Click(object sender, EventArgs e)
        {
            if (pictureBox1.Image != null)
            {
                var newForm = new Form();

                PictureBox pb = new PictureBox();
                pb.Dock = DockStyle.Fill;
                pb.Image = pictureBox1.Image;
                pb.SizeMode = PictureBoxSizeMode.Zoom;

                newForm.Size = pictureBox1.Image.Size;
                newForm.Icon = Resources.FModel;
                newForm.Text = ThePak.CurrentUsedItem;
                newForm.StartPosition = FormStartPosition.CenterScreen;
                newForm.Controls.Add(pb);
                newForm.Show();
            }
        }
        private void saveImageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (pictureBox1.Image != null)
            {
                SaveFileDialog saveTheDialog = new SaveFileDialog();
                saveTheDialog.Title = @"Save Icon";
                saveTheDialog.Filter = @"PNG Files (*.png)|*.png";
                saveTheDialog.InitialDirectory = App.DefaultOutputPath + "\\Icons\\";
                saveTheDialog.FileName = ThePak.CurrentUsedItem;
                if (saveTheDialog.ShowDialog() == DialogResult.OK)
                {
                    pictureBox1.Image.Save(saveTheDialog.FileName, ImageFormat.Png);
                    AppendText(ThePak.CurrentUsedItem, Color.DarkRed);
                    AppendText(" successfully saved", Color.Black, true);
                }
            }
        }
        private async void mergeImagesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await Task.Run(() => {
                AskMergeImages();
            });
        }
        #endregion
    }
}