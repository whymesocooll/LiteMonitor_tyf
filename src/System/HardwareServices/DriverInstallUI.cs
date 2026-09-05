using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LiteMonitor; // UpdateDialog / DownloadContext
using LiteMonitor.src.Core;

namespace LiteMonitor.src.SystemServices
{
    /// <summary>
    /// 驱动安装流程的 UI 交互（原内嵌在 DriverInstaller 中，剥离后服务类不再直接弹窗）。
    /// 只负责"怎么弹"，不负责"弹什么"——文案仍由 DriverInstaller 按语言拼接。
    /// </summary>
    internal static class DriverInstallUI
    {
        internal static void ShowMessageBox(string msg, string title, MessageBoxIcon icon)
        {
            MessageBox.Show(msg, title, MessageBoxButtons.OK, icon);
        }

        /// <summary>
        /// PawnIO 安装后需要重启系统的提示
        /// </summary>
        internal static void ShowRestartRequired(bool isChinese, string description)
        {
            ShowMessageBox(description + (isChinese
                    ? "\n请重启电脑后再打开 LiteMonitor。"
                    : "\nPlease restart Windows before opening LiteMonitor again."),
                isChinese ? "需要重启" : "Restart Required",
                MessageBoxIcon.Information);
        }

        /// <summary>
        /// 在 UI 线程显示下载对话框（无主窗体时回退到专用 STA 线程）
        /// </summary>
        internal static Task<bool> ShowDownloadDialog(Settings cfg, DownloadContext context)
        {
            var tcs = new TaskCompletionSource<bool>();

            void Show()
            {
                try
                {
                    using var dlg = new UpdateDialog(context, cfg);
                    var result = dlg.ShowDialog();
                    tcs.TrySetResult(result == DialogResult.OK);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }

            if (Application.OpenForms.Count > 0)
            {
                var form = Application.OpenForms[0];
                if (form != null && !form.IsDisposed && form.IsHandleCreated)
                {
                    if (form.InvokeRequired) form.Invoke(new Action(Show));
                    else Show();
                    return tcs.Task;
                }
            }

            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                Show();
            }
            else
            {
                var thread = new Thread(() => Show());
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
            }

            return tcs.Task;
        }
    }
}
