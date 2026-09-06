using System.Net;
using System.Net.Http;
using BluetoothMonitor.Services;

namespace BluetoothMonitor.Tests;

[TestClass]
public sealed class UpdateServiceTests
{
    [TestMethod]
    public void IsCandidateNewer_ExactSameTag_ReturnsFalse()
    {
        Assert.IsFalse(UpdateService.IsCandidateNewer("2026.09.06-4612658", "2026.09.06-4612658"));
        Assert.IsFalse(UpdateService.IsCandidateNewer("2026.09.06-4612658", "v2026.09.06-4612658"));
        Assert.IsFalse(UpdateService.IsCandidateNewer("v2026.09.06-4612658", "2026.09.06-4612658"));
    }

    [TestMethod]
    public void IsCandidateNewer_NewerDate_ReturnsTrue()
    {
        Assert.IsTrue(UpdateService.IsCandidateNewer("2026.09.05-1111111", "2026.09.06-2222222"));
        Assert.IsTrue(UpdateService.IsCandidateNewer("2026.08.30-1111111", "2026.09.01-2222222"));
    }

    [TestMethod]
    public void IsCandidateNewer_OlderDate_ReturnsFalse()
    {
        Assert.IsFalse(UpdateService.IsCandidateNewer("2026.09.06-2222222", "2026.09.05-1111111"));
        Assert.IsFalse(UpdateService.IsCandidateNewer("2026.09.01-2222222", "2026.08.30-1111111"));
    }

    [TestMethod]
    public void IsCandidateNewer_SameDateDifferentSha_ReturnsTrue()
    {
        Assert.IsTrue(UpdateService.IsCandidateNewer("2026.09.06-1111111", "2026.09.06-2222222"));
    }

    [TestMethod]
    public void IsCandidateNewer_InformationalVersionWithMatchingSha_ReturnsFalse()
    {
        Assert.IsFalse(UpdateService.IsCandidateNewer("1.0.0+4612658abcdef", "2026.09.06-4612658"));
    }

    [TestMethod]
    public void IsCandidateNewer_InformationalVersionWithDifferentSha_ReturnsTrue()
    {
        Assert.IsTrue(UpdateService.IsCandidateNewer("1.0.0+1111111abcdef", "2026.09.06-4612658"));
    }

    [TestMethod]
    public void IsCandidateNewer_SemVerComparison()
    {
        Assert.IsTrue(UpdateService.IsCandidateNewer("1.0.0", "1.1.0"));
        Assert.IsTrue(UpdateService.IsCandidateNewer("v1.0.0", "v1.0.1"));
        Assert.IsFalse(UpdateService.IsCandidateNewer("1.2.0", "1.1.0"));
        Assert.IsFalse(UpdateService.IsCandidateNewer("1.0.0", "1.0.0"));
    }

    [TestMethod]
    public async Task CheckForUpdatesAsync_ValidReleaseWithExe_FindsUpdate()
    {
        var jsonResponse = """
        {
          "tag_name": "2026.09.07-abcdef1",
          "name": "2026.09.07 (abcdef1)",
          "body": "Fixed bugs and added features.",
          "published_at": "2026-09-07T12:00:00Z",
          "assets": [
            {
              "name": "BluetoothMonitor.exe",
              "browser_download_url": "https://github.com/ttarner/bluetooth-monitor/releases/download/2026.09.07-abcdef1/BluetoothMonitor.exe",
              "size": 52428800
            }
          ]
        }
        """;

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, jsonResponse);
        using var client = new HttpClient(handler);
        var service = new UpdateService("test/repo", client);

        var result = await service.CheckForUpdatesAsync();

        Assert.IsNotNull(result.Update);
        Assert.AreEqual("2026.09.07-abcdef1", result.Update.TagName);
        Assert.AreEqual("2026.09.07 (abcdef1)", result.Update.ReleaseName);
        Assert.AreEqual("https://github.com/ttarner/bluetooth-monitor/releases/download/2026.09.07-abcdef1/BluetoothMonitor.exe", result.Update.DownloadUrl);
        Assert.AreEqual(52428800, result.Update.FileSizeBytes);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public async Task CheckForUpdatesAsync_NoExeAsset_ReturnsError()
    {
        var jsonResponse = """
        {
          "tag_name": "2026.09.07-abcdef1",
          "name": "Release",
          "assets": [
            {
              "name": "other_file.txt",
              "browser_download_url": "https://example.com/other_file.txt",
              "size": 100
            }
          ]
        }
        """;

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, jsonResponse);
        using var client = new HttpClient(handler);
        var service = new UpdateService("test/repo", client);

        var result = await service.CheckForUpdatesAsync();

        Assert.IsFalse(result.UpdateAvailable);
        Assert.IsNull(result.Update);
        Assert.IsNotNull(result.ErrorMessage);
        StringAssert.Contains(result.ErrorMessage, "No executable asset found");
    }

    private sealed class MockHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            };
            return Task.FromResult(response);
        }
    }
}

