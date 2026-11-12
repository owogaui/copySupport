using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

public class Snippet {
    public string Name, Content;
}
public class Settings {
    public int Width = 470, Height = 430;
    public List<Snippet> Items = new List<Snippet>();
}
class Picker : Form {
    const int KeyboardHook = 13;
    const int WmKeyDown = 0x100, WmSysKeyDown = 0x104;
    delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
    [DllImport("user32", SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id, Hook callback, IntPtr module, uint thread);
    [DllImport("user32")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32", CharSet=CharSet.Auto)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32")] static extern bool IsWindow(IntPtr window);

    readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickPaste123", "settings.xml");
    readonly XmlSerializer serializer = new XmlSerializer(typeof(Settings));
    readonly FlowLayoutPanel list = new FlowLayoutPanel();
    readonly NotifyIcon tray = new NotifyIcon();
    readonly Hook callback;
    readonly bool[] held = new bool[3];
    int suppressedKey;
    bool latched, exiting, pasting;
    IntPtr hook, target;
    Settings settings;

    [STAThread] static void Main(string[] args) {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 1 && args[0] == "--self-test") {
            using (var picker = new Picker(true)) picker.VerifyStorage();
            return;
        }
        bool created;
        using (var instance = new Mutex(true, @"Local\QuickPaste123", out created)) {
            if (!created) return;
            using (var picker = new Picker(false)) Application.Run(picker);
        }
    }

    Picker(bool test) {
        callback = OnKey;
        settings = test ? Defaults() : ReadSettings();
        Text = "123 快速貼上";
        Font = new Font("Microsoft JhengHei UI", 10);
        MinimumSize = new Size(350, 270);
        Size = new Size(Math.Max(350, Math.Min(settings.Width, Screen.PrimaryScreen.WorkingArea.Width)),
                        Math.Max(270, Math.Min(settings.Height, Screen.PrimaryScreen.WorkingArea.Height)));
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 47, Padding = new Padding(8, 5, 0, 0) };
        var add = new Button { Text = "＋ 新增文字", AutoSize = true, Height = 31 };
        add.Click += (s,e) => Edit(-1);
        toolbar.Controls.Add(add);
        var hint = new Label { Text = "同時按住 1、2、3", AutoSize = true, Padding = new Padding(8, 8, 0, 0) };
        toolbar.Controls.Add(hint);
        list.Dock = DockStyle.Fill;
        list.FlowDirection = FlowDirection.TopDown;
        list.WrapContents = false;
        list.AutoScroll = true;
        list.Padding = new Padding(8);
        list.SizeChanged += (s,e) => FitRows();
        Controls.Add(list);
        Controls.Add(toolbar);
        RenderItems();
        if (test) return;
        tray.Icon = SystemIcons.Application;
        tray.Text = Text;
        tray.ContextMenuStrip = new ContextMenuStrip();
        tray.ContextMenuStrip.Items.Add("開啟", null, (s,e) => OpenPicker(GetForegroundWindow()));
        tray.ContextMenuStrip.Items.Add("結束", null, (s,e) => { exiting = true; Close(); });
        tray.DoubleClick += (s,e) => OpenPicker(GetForegroundWindow());
        tray.Visible = true;
        Shown += (s,e) => {
            hook = SetWindowsHookEx(KeyboardHook, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) MessageBox.Show(this, "無法啟用 123 快捷鍵，仍可從通知區開啟。", Text);
        };
        ResizeEnd += (s,e) => SaveSize();
        FormClosing += (s,e) => {
            if (e.CloseReason == CloseReason.UserClosing && !exiting) { e.Cancel = true; HidePicker(); }
        };
        KeyDown += (s,e) => { if (e.KeyCode == Keys.Escape) { e.Handled = true; HidePicker(); } };
    }

    IntPtr OnKey(int code, IntPtr message, IntPtr data) {
        if (code < 0) return CallNextHookEx(hook, code, message, data);
        int key = Marshal.ReadInt32(data);
        int flags = Marshal.ReadInt32(data, 8);
        if ((flags & 16) != 0 || key < 49 || key > 51)
            return CallNextHookEx(hook, code, message, data);
        bool down = message.ToInt32() == WmKeyDown || message.ToInt32() == WmSysKeyDown;
        held[key - 49] = down;
        if (!down) latched = false;
        if (key == suppressedKey) {
            if (!down) suppressedKey = 0;
            return new IntPtr(1);
        }
        if (down && !latched && !Visible && !pasting && suppressedKey == 0 && held[0] && held[1] && held[2]
            && !Down(0x10) && !Down(0x11) && !Down(0x12) && !Down(0x5B) && !Down(0x5C)) {
            latched = true;
            suppressedKey = key;
            IntPtr original = GetForegroundWindow();
            BeginInvoke((Action)(() => OpenPicker(original)));
            return new IntPtr(1);
        }
        return CallNextHookEx(hook, code, message, data);
    }
    static bool Down(int key) { return GetAsyncKeyState(key) < 0; }

    void OpenPicker(IntPtr original) {
        if (pasting) return;
        if (!Visible) target = original == Handle ? IntPtr.Zero : original;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }
    void HidePicker() {
        SaveSize();
        Hide();
        if (IsWindow(target)) SetForegroundWindow(target);
    }
    async void Paste(string content) {
        if (pasting) return;
        pasting = true;
        try {
            Clipboard.SetDataObject(content, true, 8, 35);
            IntPtr original = target;
            HidePicker();
            if (!IsWindow(original)) { CopiedOnly(); return; }
            for (int attempt = 0; attempt < 24; attempt++) {
                await System.Threading.Tasks.Task.Delay(25);
                if (GetForegroundWindow() != original) break;
                if (held[0] || held[1] || held[2] || Down(0x10) || Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C)) continue;
                if (NativePaste.Send()) return;
                break;
            }
            CopiedOnly();
        } catch (ExternalException) {
            MessageBox.Show(this, "剪貼簿暫時無法使用，請再試一次。", Text);
        } finally { pasting = false; }
    }
    void CopiedOnly() { tray.ShowBalloonTip(2200, Text, "已複製，請在輸入框按 Ctrl + V。", ToolTipIcon.Info); }

    void RenderItems() {
        list.SuspendLayout();
        while (list.Controls.Count != 0) list.Controls[0].Dispose();
        if (settings.Items.Count == 0) list.Controls.Add(new Label { Text = "使用「新增文字」建立第一筆項目。", AutoSize = true });
        for (int i = 0; i < settings.Items.Count; i++) {
            int index = i;
            Snippet item = settings.Items[index];
            var row = new TableLayoutPanel { Height = 44, ColumnCount = 3, RowCount = 1 };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            var paste = new Button { Text = item.Name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, AutoEllipsis = true };
            var edit = new Button { Text = "編輯", Dock = DockStyle.Fill };
            var delete = new Button { Text = "刪除", Dock = DockStyle.Fill };
            paste.Click += (s,e) => Paste(item.Content);
            edit.Click += (s,e) => Edit(index);
            delete.Click += (s,e) => {
                if (MessageBox.Show(this, "刪除「" + item.Name + "」？", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                settings.Items.RemoveAt(index);
                if (!TrySave()) settings.Items.Insert(index, item);
                RenderItems();
            };
            row.Controls.Add(paste, 0, 0);
            row.Controls.Add(edit, 1, 0);
            row.Controls.Add(delete, 2, 0);
            list.Controls.Add(row);
        }
        FitRows();
        list.ResumeLayout();
    }
    void FitRows() {
        foreach (Control row in list.Controls)
            if (row is TableLayoutPanel) row.Width = Math.Max(200, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 23);
    }
    void Edit(int index) {
        Snippet old = index < 0 ? new Snippet() : settings.Items[index];
        using (var dialog = new Form { Text = index < 0 ? "新增文字" : "編輯文字", Font = Font, Size = new Size(540, 410), MinimumSize = new Size(350, 280), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false }) {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            var name = new TextBox { Text = old.Name, Dock = DockStyle.Fill };
            var content = new TextBox { Text = old.Content, Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Vertical };
            var save = new Button { Text = "儲存", Anchor = AnchorStyles.Right, Width = 90, Height = 32 };
            layout.Controls.Add(new Label { Text = "名稱", AutoSize = true }, 0, 0);
            layout.Controls.Add(name, 0, 1);
            layout.Controls.Add(new Label { Text = "貼上的內容", AutoSize = true }, 0, 2);
            layout.Controls.Add(content, 0, 3);
            layout.Controls.Add(save, 0, 4);
            save.Click += (s,e) => {
                if (string.IsNullOrWhiteSpace(name.Text) || content.Text.Length == 0) {
                    MessageBox.Show(dialog, "請輸入名稱和內容。", Text); return;
                }
                var item = new Snippet { Name = name.Text.Trim(), Content = content.Text };
                if (index < 0) settings.Items.Add(item); else settings.Items[index] = item;
                if (TrySave()) { dialog.DialogResult = DialogResult.OK; return; }
                if (index < 0) settings.Items.RemoveAt(settings.Items.Count - 1); else settings.Items[index] = old;
            };
            dialog.Controls.Add(layout);
            if (dialog.ShowDialog(this) == DialogResult.OK) RenderItems();
        }
    }
    static Settings Defaults() {
        var data = new Settings();
        data.Items.Add(new Snippet { Name = "我的名字", Content = "王小明" });
        data.Items.Add(new Snippet { Name = "Prompt 1", Content = "請用繁體中文整理以下內容。\r\n列出重點和下一步。" });
        return data;
    }
    Settings ReadSettings() {
        if (!File.Exists(path)) return Defaults();
        try { using (var file = File.OpenRead(path)) return (Settings)serializer.Deserialize(file); }
        catch (Exception error) {
            MessageBox.Show("無法讀取設定，請先備份或修復檔案。\n" + path, "123 快速貼上");
            throw;
        }
    }
    void SaveSize() {
        if (WindowState != FormWindowState.Normal || (Width == settings.Width && Height == settings.Height)) return;
        int width = settings.Width, height = settings.Height;
        settings.Width = Width; settings.Height = Height;
        if (!TrySave()) { settings.Width = width; settings.Height = height; }
    }
    bool TrySave() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string pending = path + ".tmp";
            using (var file = File.Create(pending)) WriteSettings(file);
            if (File.Exists(path)) File.Replace(pending, path, null); else File.Move(pending, path);
            return true;
        } catch (Exception error) {
            MessageBox.Show(this, "無法儲存設定。\n" + error.Message, Text);
            return false;
        }
    }
    void WriteSettings(Stream stream) {
        var options = new System.Xml.XmlWriterSettings { Indent = true, NewLineHandling = System.Xml.NewLineHandling.Entitize };
        using (var writer = System.Xml.XmlWriter.Create(stream, options)) serializer.Serialize(writer, settings);
    }
    void VerifyStorage() {
        string expected = "王小明\r\n  多行文字\t";
        settings.Items[0].Content = expected;
        using (var stream = new MemoryStream()) {
            WriteSettings(stream);
            stream.Position = 0;
            var result = (Settings)serializer.Deserialize(stream);
            if (result.Items[0].Content != expected || result.Width != settings.Width) throw new Exception("Storage round trip failed.");
        }
    }
    protected override void Dispose(bool disposing) {
        if (disposing) {
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
static class NativePaste {
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] struct InputData {
        [FieldOffset(0)] public Keyboard Keyboard;
        [FieldOffset(0)] public Mouse Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] struct Keyboard {
        public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra;
    }
    [StructLayout(LayoutKind.Sequential)] struct Mouse {
        public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra;
    }
    [DllImport("user32", SetLastError=true)] static extern uint SendInput(uint count, Input[] inputs, int size);
    static Input Key(ushort key, uint flags) {
        return new Input { Type = 1, Data = new InputData { Keyboard = new Keyboard { Key = key, Flags = flags } } };
    }
    public static bool Send() {
        var keys = new[] { Key(0x11, 0), Key(0x56, 0), Key(0x56, 2), Key(0x11, 2) };
        int size = Marshal.SizeOf(typeof(Input));
        uint sent = SendInput((uint)keys.Length, keys, size);
        if (sent > 0 && sent < keys.Length) {
            var release = sent == 2 ? new[] { Key(0x56, 2), Key(0x11, 2) } : new[] { Key(0x11, 2) };
            SendInput((uint)release.Length, release, size);
        }
        return sent == keys.Length;
    }
}


