# 123 快速貼上

Windows 常駐工具，能儲存姓名、常用回覆或多行 Prompt。

1. 執行 `QuickPaste123.exe`。
2. 在視窗中按「新增文字」，填寫名稱和要貼上的內容。
3. 收起視窗後，先用滑鼠點一下目標輸入框，讓游標在裡面閃爍。
4. 同時按住鍵盤上排的 `1`、`2`、`3`，視窗出現後鬆開按鍵。
5. 點項目名稱。工具會收起，回到原本的視窗並送出 `Ctrl + V`。

最先按下的兩個數字可能已輸入原欄位。若原視窗未能取回焦點或 Windows 阻擋按鍵，文字會留在剪貼簿，可手動貼上。程式初次開啟時可先新增或編輯項目。拖曳視窗邊角可調整大小，關閉視窗或按 Esc 會收到通知區。若要完全結束，請從通知區圖示選「結束」。

文字與視窗大小儲存在 `%LOCALAPPDATA%\QuickPaste123\settings.xml`。

## 從原始碼建置

在 Windows PowerShell 執行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`。使用 Windows 的 .NET Framework 4.x 編譯器與 WinForms，不需要額外套件。輸出為同一資料夾的 `QuickPaste123.exe`。


