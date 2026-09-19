using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace Bfocus.Widget.Maui
{
    /// <summary>
    /// <c>bfocus:download</c>: Android usa o gerenciador de downloads (pasta Downloads, com
    /// notificação); iOS, Mac e Windows baixam para o cache e abrem a folha de compartilhar / salvar.
    /// </summary>
    internal static class Downloads
    {
        private static readonly Lazy<HttpClient> Http = new Lazy<HttpClient>(() => new HttpClient { Timeout = TimeSpan.FromMinutes(5) });

        public static async Task SaveAsync(string url, string fileName)
        {
#if ANDROID
            EnqueueAndroid(url, fileName, null);
            await Task.CompletedTask;
#else
            var dir = Path.Combine(FileSystem.CacheDirectory, "bfocus-downloads");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName);
            using (var input = await Http.Value.GetStreamAsync(url))
            using (var output = File.Create(path))
                await input.CopyToAsync(output);
            await Share.Default.RequestAsync(new ShareFileRequest(fileName, new ShareFile(path)));
#endif
        }

#if ANDROID
        public static void EnqueueAndroid(string url, string fileName, string? mimeType)
        {
            var ctx = Android.App.Application.Context;
            if (ctx.GetSystemService(Android.Content.Context.DownloadService) is not Android.App.DownloadManager dm) return;
            var request = new Android.App.DownloadManager.Request(Android.Net.Uri.Parse(url));
            request.SetTitle(fileName);
            request.SetNotificationVisibility(Android.App.DownloadVisibility.VisibleNotifyCompleted);
            request.SetDestinationInExternalPublicDir(Android.OS.Environment.DirectoryDownloads, FileNames.Sanitize(fileName));
            if (!string.IsNullOrEmpty(mimeType)) request.SetMimeType(mimeType);
            dm.Enqueue(request);
        }
#endif
    }
}
